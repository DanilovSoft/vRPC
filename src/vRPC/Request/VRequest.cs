using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Request = {Method.FullName}\}")]
    internal sealed class VRequest<TResult> : IVRequest, IResponseAwaiter
    {
        private readonly TaskCompletionSource<TResult?> _tcs;
        public RequestMethodMeta Method { get; }
        public object?[]? Args { get; private set; }
        public Task<TResult?> Task => _tcs.Task;
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
        private OldReusableRequestState _state = new(ReusableRequestState.ReadyToSend);

        // ctor
        internal VRequest(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(!method.IsNotificationRequest);

            Method = method;
            Args = args;
            _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public bool TryBeginSend()
        {
            // Отправляющий поток пытается атомарно забрать объект.
            var prevState = _state.SetSending();
            return prevState == ReusableRequestState.ReadyToSend;
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TrySetErrorResponse(Exception exception)
        {
            InnerTrySetErrorResponse(exception);
        }

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(header.IsRequest == false);

            TResult? result = Method.DeserializeVResponse<TResult>(in header, payload, out VRpcException? vException);

            if (vException == null)
            {
                _tcs.TrySetResult(result);
            }
            else
            {
                InnerTrySetErrorResponse(vException);
            }
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer, out int headerSize)
        {
            Debug.Assert(Args != null);

            if (Method.TrySerializeVRequest(Args, Id, out headerSize, out buffer, out var vException))
            {
                return true;
            }
            else
            {
                InnerTrySetErrorResponse(vException);
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

        private void InnerTrySetErrorResponse(Exception exception)
        {
            // Предотвратит бесмысленный TryBeginSend.
            _state.SetErrorResponse();

            _tcs.TrySetException(exception);
        }

        /// <inheritdoc/>>
        void IResponseAwaiter.TrySetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new InvalidOperationException();
        }
    }
}
