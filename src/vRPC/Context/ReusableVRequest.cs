using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableVRequest : IVRequest, IRequest
    {
        private readonly ManagedConnection _context;
        public RequestMethodMeta? Method { get; private set; }
        public object[]? Args { get; private set; }
        public int Id { get; set; }
        public bool IsNotification => false;
        private object? _tcs;
        private Action<object?, ReusableVRequest>? _setResult;
        private Action<Exception, ReusableVRequest>? _setException;

        public ReusableVRequest(ManagedConnection context)
        {
            _context = context;
        }

        private void Reset()
        {
            Method = null;
            Args = null;
            _tcs = null;
            _setResult = null;
            _setException = null;
            Id = 0;

            _context.ReleaseReusable(this);
        }

        internal Task<TResult> Initialize<TResult>(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(_tcs == null);
            Debug.Assert(_setResult == null);
            Debug.Assert(_setException == null);

            Method = method;
            Args = args;

            var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tcs = tcs;
            _setResult = SetResult<TResult>;
            _setException = SetException<TResult>;

            return tcs.Task;
        }

        private static void SetResult<TResult>(object? result, ReusableVRequest self)
        {
            var tcs = self._tcs as TaskCompletionSource<TResult>;
            Debug.Assert(tcs != null);

            // Нужно сделать сброс перед установкой результата.
            self.Reset();

            tcs.TrySetResult((TResult)result!);
        }

        private static void SetException<TResult>(Exception exception, ReusableVRequest self)
        {
            var tcs = self._tcs as TaskCompletionSource<TResult>;
            Debug.Assert(tcs != null);

            // Нужно сделать сброс перед установкой результата.
            self.Reset();

            tcs.TrySetException(exception);
        }

        public void SetException(VRpcException vException)
        {
            SetException(exception: vException);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetException(Exception exception)
        {
            Debug.Assert(_setException != null);

            _setException(exception, this);
        }

        private void SetResult(object result)
        {
            Debug.Assert(_setResult != null);

            _setResult(result, this);
        }

        public void SetJResponse(ref Utf8JsonReader reader)
        {
            Debug.Assert(false, "Сюда не должны попадать");
            throw new NotSupportedException();
        }

        public void SetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            Debug.Assert(Method != null);

            var result = Method.DeserializeVResponse(in header, payload, out VRpcException? vException);

            if (vException == null)
            {
                SetResult(result);
            }
            else
            {
                SetException(vException);
            }
        }

        public bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize)
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
                SetException(vException);
                return false;
            }
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
