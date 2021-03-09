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
        public bool IsNotification => false;

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
        public void SetErrorResponse(VRpcException rpcException)
        {
            SetErrorResponse(exception: rpcException);
        }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetErrorResponse(Exception exception)
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
                SetErrorResponse(vException);
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

        public bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize)
        {
            Debug.Assert(Args != null);

            if (Method.TrySerializeVRequest(Args, Id, out headerSize, out buffer, out var vException))
            {
                return true;
            }
            else
            {
                SetErrorResponse(vException);
                return false;
            }
        }

        public void CompleteSend(VRpcException exception)
        {
            // Игнорируем.
        }

        public void CompleteSend()
        {
            // Игнорируем.
        }

        public bool TryBeginSend()
        {
            throw new NotImplementedException();
        }
    }
}
