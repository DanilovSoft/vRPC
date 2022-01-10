using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static DanilovSoft.vRPC.ReusableRequestState;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableJRequest : IJRequest, IResponseAwaiter
    {
        private readonly object _stateObj = new();
        private object StateObj => _stateObj;
        private readonly ArrayBufferWriter<byte> _reusableMemory = new(initialize: false);
        private readonly RpcManagedConnection _context;
        public RequestMethodMeta? Method { get; private set; }
        public object?[]? Args { get; private set; }
        public int Id { get; set; }
        public bool IsNotification => false;
        private object? _tcs;
        private TrySetJResponseDelegate? _trySetResponse;
        private Func<Exception, bool>? _trySetErrorResponse;
        // Решает какой поток будет выполнять Reset.
        //private ReusableRequestState _state = new(ReusableRequestStateEnum.Reset);
        private ReusableRequestState _state = Reset;

        public ReusableJRequest(RpcManagedConnection context)
        {
            _context = context;
        }

        public Task<TResult?> Initialize<TResult>(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(_tcs == null);
            Debug.Assert(_trySetResponse == null);
            Debug.Assert(_trySetErrorResponse == null);
            Debug.Assert(_reusableMemory != null);
            Debug.Assert(_reusableMemory.IsRented == false);
            Debug.Assert(_state == Reset);

            _reusableMemory.Rent();
            Method = method;
            Args = args;

            var tcs = new TaskCompletionSource<TResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tcs = tcs;
            _trySetErrorResponse = tcs.TrySetException;
            _trySetResponse = TrySetResponse<TResult>;
            _state = ReadyToSend;

            return tcs.Task;
        }

        // Вызывается только отправляющим потоком.
        public bool TryBeginSend()
        {
            // Отправляющий поток пытается забрать объект.
            lock (StateObj)
            {
                if (_state == ReadyToSend)
                {
                    _state = Sending;
                    return true;
                }
                else
                    return false;
            }
        }

        // Метод может быть вызван и читающим потоком, и отправляющим одновременно!
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrySetErrorResponse(Exception vException)
        {
            InnerTrySetErrorResponse(vException);
        }

        // Вызывается только читающим потоком.
        public void TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(_trySetResponse != null);

            _trySetResponse(ref reader);
        }

        // TODO не диспозить буфер снаружи - race condition!
        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? reusableBuffer)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            var args = Args;
            Args = null;

            if (JsonRpcSerializer.TrySerializeRequest(Method.FullName, args, Id, _reusableMemory, out var exception))
            {
                reusableBuffer = _reusableMemory;
                return true;
            }
            else
            {
                _reusableMemory.Return();
                InnerTrySetErrorResponse(exception);
                reusableBuffer = null;
                return false;
            }
        }

        // Вызывается только отправляющим потоком.
        public void CompleteSend()
        {
            lock (StateObj)
            {
                Debug.Assert(_state is Sending or GotResponse or GotErrorResponse);

                switch (_state)
                // Успешно отправили запрос.
                {
                    case Sending:
                        ReturnReusableMemory();
                        _state = WaitingResponse;
                        break;
                    case GotResponse or GotErrorResponse:
                        // Другой поток не сбросил потому что видел что мы ещё отправляем.
                        Reuse();
                        break;
                }
            }
        }

        // Вызывается только отправляющим потоком.
        public void CompleteSend(VRpcException exception)
        {
            lock (StateObj)
            {
                // CompleteSend() должен вызвать Reuse().
                _state = GotErrorResponse;
            }
        }

        private void InnerTrySetErrorResponse(Exception vException)
        {
            var trySetErrorResponse = _trySetErrorResponse;
            Debug.Assert(trySetErrorResponse != null);
            Debug.Assert(_state is ReadyToSend or Sending or WaitingResponse);

            lock (StateObj)
            {
                switch (_state)
                // Ответственность на сбросе на нас.
                {
                    case WaitingResponse:
                        Reuse();
                        break;
                    case ReadyToSend:
                        // Ответственность на сбросе на нас.
                        Reuse();
                        break;
                    case Sending:
                        _state = GotErrorResponse;
                        break;
                }
            }
            trySetErrorResponse(vException);
        }

        /// <summary>
        /// Арендованый буфер уже должен быть возвращён.
        /// </summary>
        /// <remarks>Переводит статус в Reset.</remarks>
        private void Reuse()
        {
            Debug.Assert(Monitor.IsEntered(StateObj));
            Debug.Assert(_state is WaitingResponse or GotResponse or GotErrorResponse);

            Id = 0;
            Method = null;
            Args = null;
            _tcs = null;
            _trySetResponse = null;
            _trySetErrorResponse = null;

            ReturnReusableMemory();

            _state = Reset;
            _context.AtomicRestoreReusableJ(this);
        }

        // Вызывается только читающим потоком.
        private void TrySetResponse<TResult>(ref Utf8JsonReader reader)
        {
            Debug.Assert(Method != null);

            if (Method.ReturnType != typeof(VoidStruct))
            // Поток ожидает некий объект как результат.
            {
                TResult? result;
                try
                {
                    // Шаблонный сериализатор экономит на упаковке.
                    result = JsonSerializer.Deserialize<TResult>(ref reader);
                }
                catch (JsonException deserializationException)
                {
                    // Сообщить ожидающему потоку что произошла ошибка при разборе ответа для него.
                    InnerTrySetErrorResponse(new VRpcProtocolErrorException(
                        $"Ошибка десериализации ответа на запрос \"{Method.FullName}\".", deserializationException));

                    return;
                }
                TrySetResponse(result);
            }
            else
            // void.
            {
                TrySetResponse(default(VoidStruct));
            }
        }

        // Вызывается только читающим потоком.
        private void TrySetResponse<TResult>(TResult? result)
        {
            var tcs = _tcs as TaskCompletionSource<TResult?>;
            Debug.Assert(tcs != null);
            Debug.Assert(_state is Sending or WaitingResponse);

            lock (StateObj)
            {
                switch (_state)
                {
                    case WaitingResponse:
                        // Перед установкой результата нужно сделать объект снова доступным для переиспользования.
                        Reuse();
                        break;
                    case Sending:
                        _state = GotResponse;
                        break;
                }
            }
            tcs.TrySetResult(result);
        }

        private void ReturnReusableMemory()
        {
            if (_reusableMemory.IsRented)
            {
                _reusableMemory.Return();
            }
        }

        void IResponseAwaiter.TrySetVResponse(in HeaderDto _, ReadOnlyMemory<byte> __)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }
    }
}
