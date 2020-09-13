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
    internal sealed class VRequest<TResult> : IMessageToSend, IVRequest, IRequest
    {
        private readonly TaskCompletionSource<TResult> _tcs;
        public RequestMethodMeta Method { get; }
        public object[]? Args { get; private set; }
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

        // ctor
        internal VRequest(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(!method.IsNotificationRequest);

            Method = method;
            Args = args;
            _tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        public void SetException(VRpcException rpcException)
        {
            SetException(exception: rpcException);
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            Debug.Assert(_tcs != null);

            _tcs.TrySetException(exception);
        }

        private void SetResult([AllowNull] TResult result)
        {
            Debug.Assert(_tcs != null);

            _tcs.TrySetResult(result!);
        }

        //private void DeserializeResponse(ReadOnlyMemory<byte> payload, string? contentEncoding)
        //{
        //    TResult result = contentEncoding switch
        //    {
        //        KnownEncoding.ProtobufEncoding => ExtensionMethods.DeserializeProtoBuf<TResult>(payload),
        //        _ => ExtensionMethods.DeserializeJson<TResult>(payload.Span), // Сериализатор по умолчанию.
        //    };
        //    SetResult(result);
        //}

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        public void SetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(header.IsRequest == false);

            var result = Method.DeserializeVResponse(in header, payload, out VRpcException? vException);

            if (vException == null)
            {
                SetResult((TResult)result);
            }
            else
            {
                SetException(vException);
            }
        }

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        public void SetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new InvalidOperationException();
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
                SetException(vException);
                return false;
            }
        }

        public TResult GetResult(short token)
        {
            throw new NotImplementedException();
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            throw new NotImplementedException();
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            throw new NotImplementedException();
        }

        public void CompleteNotification(VRpcException exception)
        {
            // Игнорируем.
        }

        public void CompleteNotification()
        {
            // Игнорируем.
        }
    }
}
