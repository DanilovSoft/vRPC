using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableVRequest : IVRequest, IResponseAwaiter
    {
        private readonly RpcManagedConnection _context;
        public RequestMethodMeta? Method { get; private set; }
        public object?[]? Args { get; private set; }
        public int Id { get; set; }
        public bool IsNotification => false;
        private object? _tcs;
        private TrySetVResponseDelegate? _trySetResponse;
        private Func<Exception, bool>? _trySetErrorResponse;
        // Решает какой поток будет выполнять Reset.
        private ReusableRequestState _state = new(ReusableRequestStateEnum.Reset);

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
            _state.SetReady();

            return tcs.Task;
        }

        /// <summary>Переводит в состояние 3.</summary>
        public bool TryBeginSend()
        {
            // Отправляющий поток пытается атомарно забрать объект.
            var prevState = _state.TrySetSending();
            return prevState == ReusableRequestStateEnum.ReadyToSend;
        }

        /// <summary>Переводит в состояние 4.</summary>
        public void CompleteSend()
        {
            var prevState = _state.TrySetSended();

            // Во время отправки уже мог прийти ответ и поменять статус или могла произойти ошибка.
            if (prevState is ReusableRequestStateEnum.GotResponse or ReusableRequestStateEnum.GotErrorResponse)
            // Ответственность на сбросе на нас.
            {
                Reset();
            }
        }

        /// <summary>Переводит в состояние 6.</summary>
        public void CompleteSend(VRpcException exception)
        {
            _state.SetErrorResponse();
            Reset();
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

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer, out int headerSize)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            var args = Args;
            Args = null;

            if (Method.TrySerializeVRequest(args, Id, out headerSize, out buffer, out var vException))
            {
                return true;
            }
            else
            {
                InnerTrySetErrorResponse(vException);
                return false;
            }
        }

        /// <summary>Переводит в состояние 1.</summary>
        private void Reset()
        {
            Debug.Assert(_state.State
                is ReusableRequestStateEnum.GotResponse
                or ReusableRequestStateEnum.GotErrorResponse);

            Id = 0;
            Method = null;
            Args = null;
            _tcs = null;
            _trySetResponse = null;
            _trySetErrorResponse = null;

            _state.Reset();
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

            var prevState = _state.SetGotResponse();

            // В редких случаях мы могли обогнать отправляющий поток,
            // в этом случае сброс сделает отправляющий поток.
            if (prevState != ReusableRequestStateEnum.Sending)
            {
                // Нужно сделать сброс перед установкой результата.
                Reset();
            }
            tcs.TrySetResult(result!);
        }

        private void InnerTrySetErrorResponse(Exception exception)
        {
            var trySetErrorResponse = _trySetErrorResponse;
            Debug.Assert(trySetErrorResponse != null);

            // Атомарно узнаём в каком состоянии было сообщение.
            var prevState = _state.SetErrorResponse();

            Debug.Assert(prevState
                is ReusableRequestStateEnum.ReadyToSend
                or ReusableRequestStateEnum.Sending
                or ReusableRequestStateEnum.Sended);

            // upd: Может быть случай когда читающий поток завершается
            // ошибкой ещё до отправки сообщения отправляющим потоком.
            if (prevState != ReusableRequestStateEnum.Sending)
            {
                // Нужно сделать сброс перед установкой результата
                // иначе другой поток может начать переиспользование это-го класса.
                Reset();
            }

            trySetErrorResponse(exception);
        }

        void IResponseAwaiter.TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }
    }
}
