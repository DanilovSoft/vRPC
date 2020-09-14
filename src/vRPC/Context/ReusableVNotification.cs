using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableVNotification : INotification, IValueTaskSource
    {
        public RequestMethodMeta? Method { get; private set; }
        public object[]? Args { get; private set; }
        public bool IsNotification => true;
        private ManualResetValueTaskSourceCore<VoidStruct> _mrv;

        /// <summary>
        /// Для создания синглтона.
        /// </summary>
        internal ReusableVNotification() 
        {
            _mrv.RunContinuationsAsynchronously = true;   
        }

        internal void Initialize(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(!method.IsJsonRpc);
            Debug.Assert(method.IsNotificationRequest);

            Method = method;
            Args = args;
        }

        public ValueTask WaitNotificationAsync()
        {
            return new ValueTask(this, _mrv.Version);
        }

        private void SetException(VRpcException exception)
        {
            _mrv.SetException(exception);
        }

        public bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize)
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            if (Method.TrySerializeVRequest(Args, id: null, out headerSize, out buffer, out var vException))
            {
                return true;
            }
            else
            {
                SetException(vException);
                return false;
            }
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return _mrv.GetStatus(token);
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            _mrv.OnCompleted(continuation, state, token, flags);
        }

        public void GetResult(short token)
        {
            _mrv.GetResult(token);
            _mrv.Reset();
        }

        public void CompleteNotification(VRpcException exception)
        {
            _mrv.SetException(exception);
        }

        public void CompleteNotification()
        {
            _mrv.SetResult(default);
        }
    }
}
