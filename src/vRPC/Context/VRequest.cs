using DanilovSoft.vRPC.Context;
using DanilovSoft.vRPC.Source;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Request = {Method.FullName}\}")]
    internal sealed class VRequest<TResult> : IVRequest, IResponseAwaiter
    {
        private readonly TaskCompletionSource<TResult> _tcs;
        public RequestMethodMeta Method { get; }
        public object?[]? Args { get; private set; }
        public Task<TResult> Task => _tcs.Task;
#if DEBUG
        [SuppressMessage("CodeQuality", "IDE0051:Удалите неиспользуемые закрытые члены", Justification = "Для отладчика")]
        private object? ValueForDebugDisplay
        {
            get
            {
                if (_tcs.Task.IsCompleted)
                {
                    return _tcs.Task.Exception ?? _tcs.Task.Result as object;
                }
                else
                {
                    return "Pending...";
                }
            }
        }
#endif
        public int Id { get; set; }
        public bool IsNotification => false;
        private ReusableRequestState _state = new(ReusableRequestStateEnum.ReadyToSend);

        // ctor
        internal VRequest(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(!method.IsNotificationRequest);

            Method = method;
            Args = args;
            _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public bool TryBeginSend()
        {
            // Отправляющий поток пытается атомарно забрать объект.
            var prevState = _state.TrySetSending();
            return prevState == ReusableRequestStateEnum.ReadyToSend;
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        public void TrySetErrorResponse(Exception exception)
        {
            // Предотвратит бесмысленный TryBeginSend.
            _state.SetErrorResponse();

            _tcs.TrySetException(exception);
        }

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(header.IsRequest == false);

            var result = Method.DeserializeVResponse(in header, payload, out VRpcException? vException);

            if (vException == null)
            {
                TrySetResult((TResult)result!);
            }
            else
            {
                TrySetErrorResponse(vException);
            }
        }

        public bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize)
        {
            Debug.Assert(Args != null);

            if (Method.TrySerializeVRequest(Args, Id, out headerSize, out buffer, out var vException))
            {
                return true;
            }
            else
            {
                TrySetErrorResponse(vException);
                return false;
            }
        }

        // Вызывает отправляющий поток.
        public void CompleteSend(VRpcException exception)
        {
            // Игнорируем.
        }

        // Вызывает отправляющий поток.
        public void CompleteSend()
        {
            // Игнорируем.
        }

        private void TrySetResult(TResult result)
        {
            Debug.Assert(_tcs != null);

            _tcs.TrySetResult(result);
        }

        /// <inheritdoc/>>
        void IResponseAwaiter.TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new InvalidOperationException();
        }
    }
}
