using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static DanilovSoft.vRPC.ReusableRequestState;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableVRequest : IVRequest, IResponseAwaiter
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
        private TrySetVResponseDelegate? _trySetResponse;
        private Func<Exception, bool>? _trySetErrorResponse;
        // Решает какой поток будет выполнять Reset.
        private ReusableRequestState _state = Reset;

        public ReusableVRequest(RpcManagedConnection context)
        {
            _context = context;
        }

        /// <summary>Переводит в состояние 2.</summary>
        public Task<TResult?> Initialize<TResult>(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(_tcs == null);
            Debug.Assert(_trySetResponse == null);
            Debug.Assert(_trySetErrorResponse == null);

            Method = method;
            Args = args;

            var tcs = new TaskCompletionSource<TResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tcs = tcs;
            _trySetErrorResponse = tcs.TrySetException;
            _trySetResponse = TrySetResponse<TResult>;
            _state = ReadyToSend;

            return tcs.Task;
        }

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

        /// <summary>Переводит в состояние 4.</summary>
        public void CompleteSend()
        {
            lock (StateObj)
            {
                if (_state == Sending)
                // Успешно отправили запрос.
                {
                    ReturnReusableMemory();
                }
                else if (_state is GotResponse or GotErrorResponse)
                // Во время отправки произошла ошибка или уже пришел ответ и обогнал нас.
                {
                    ReturnReusableMemory();

                    // Другой поток не сбросил потому что видел что мы ещё отправляем.
                    Reuse();
                }
#if DEBUG
                else
                {
                    Debug.Assert(false);
                }
#endif
                _state = WaitingResponse;
            }
        }

        /// <summary>Переводит в состояние 6.</summary>
        public void CompleteSend(VRpcException exception)
        {
            lock (StateObj)
            {
                Debug.Assert(_state is Sending or GotResponse or GotErrorResponse);

                if (_state == Sending)
                // Успешно отправили запрос.
                {
                    ReturnReusableMemory();
                }
                else if (_state is GotResponse or GotErrorResponse)
                // Во время отправки произошла ошибка или уже пришел ответ и обогнал нас.
                {
                    // Другой поток не сбросил потому что видел что мы ещё отправляем.
                    Reuse();
                }
                _state = WaitingResponse;
            }
        }

        // Метод может быть вызван и читающим потоком, и отправляющим одновременно!
        /// <summary>
        /// При получении ответа с ошибкой или при обрыве соединения что технически считается результатом на запрос.
        /// </summary>
        /// <remarks>Переводит в состояние 6.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrySetErrorResponse(Exception exception)
        {
            InnerTrySetErrorResponse(exception);
        }

        public void TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(_trySetResponse != null);

            _trySetResponse(in header, payload);
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? reusableMemory, out int headerSize)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            var args = Args;
            Args = null;

            if (Method.TrySerializeVRequest(args, Id, out headerSize, _reusableMemory, out var vException))
            {
                reusableMemory = _reusableMemory;
                return true;
            }
            else
            {
                _reusableMemory.Return();
                InnerTrySetErrorResponse(vException);
                reusableMemory = null;
                return false;
            }
        }

        /// <summary>Переводит в состояние 1.</summary>
        private void Reuse()
        {
            Debug.Assert(Monitor.IsEntered(StateObj));
            Debug.Assert(_state is GotResponse or GotErrorResponse);

            Id = 0;
            Method = null;
            Args = null;
            _tcs = null;
            _trySetResponse = null;
            _trySetErrorResponse = null;

            _state = Reset;
            _context.AtomicRestoreReusableV(this);
        }

        /// <summary>Переводит в состояние 5.</summary>
        private void TrySetResponse<TResult>(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(Method != null);

            TResult? result = Method.DeserializeVResponse<TResult>(in header, payload, out VRpcException? vException);

            if (vException == null)
            {
                TrySetResponse<TResult>(result);
            }
            else
            {
                InnerTrySetErrorResponse(vException);
            }
        }

        /// <summary>Переводит в состояние 5.</summary>
        private void TrySetResponse<TResult>(TResult? result)
        {
            var tcs = _tcs as TaskCompletionSource<TResult?>;
            Debug.Assert(tcs != null);
            Debug.Assert(_state is Sending or WaitingResponse);

            lock (StateObj)
            {
                if (_state == WaitingResponse)
                {
                    // Перед установкой результата нужно сделать объект снова доступным для переиспользования.
                    Reuse();
                }
#if DEBUG
                else if (_state == Sending)
                // Мы обогнали отправляющий поток. В этом случае сброс сделает отправляющий поток.
                {

                }
#endif
                _state = GotResponse;
            }
            tcs.TrySetResult(result!);
        }

        private void InnerTrySetErrorResponse(Exception exception)
        {
            var trySetErrorResponse = _trySetErrorResponse;
            Debug.Assert(trySetErrorResponse != null);
            Debug.Assert(_state is ReadyToSend or Sending or WaitingResponse);

            lock (StateObj)
            {
                if (_state == WaitingResponse)
                // Ответственность на сбросе на нас.
                {
                    // Буффер уже освободил отправляющий поток.
                    Debug.Assert(_reusableMemory.IsRented == false);

                    Reuse();
                }
                else if (_state == ReadyToSend)
                // Отправка еще не началась и мы успели её предотвратить.
                {
                    Debug.Assert(_reusableMemory.IsRented);

                    // Буффер был заряжен.
                    _reusableMemory.Return();

                    // Ответственность на сбросе на нас.
                    Reuse();
                }
                else if (_state == Sending)
                {
                    // Не можем сделать сброс потому что другой поток еще отправляет данные -> он сам сделает сброс.
                }
                _state = GotErrorResponse;
            }
            trySetErrorResponse(exception);
        }

        private void ReturnReusableMemory()
        {
            //Debug.Assert(_reusableMemory.IsRented);

            if (_reusableMemory.IsRented)
                _reusableMemory.Return();
        }

        void IResponseAwaiter.TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }
    }
}
