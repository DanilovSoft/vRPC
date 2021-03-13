using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableJNotification : IJRequest, INotification, IValueTaskSource
    {
        public RequestMethodMeta? Method { get; private set; }
        public object?[]? Args { get; private set; }
        public bool IsNotification => true;
        private ManualResetValueTaskSourceCore<VoidStruct> _mrv;

        public ReusableJNotification()
        {
            _mrv.RunContinuationsAsynchronously = true;
        }

        internal void Initialize(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(method.IsJsonRpc);
            Debug.Assert(method.IsNotificationRequest);

            Method = method;
            Args = args;
        }

        public ValueTask WaitNotificationAsync()
        {
            return new ValueTask(this, _mrv.Version);
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer)
        {
            Debug.Assert(Method != null);
            Debug.Assert(Args != null);

            var args = Args;
            Args = null;

            if (JsonRpcSerializer.TrySerializeNotification(Method.FullName, args, out buffer, out var exception))
            {
                return true;
            }
            else
            {
                _mrv.SetException(exception);
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

        public void CompleteSend(VRpcException exception)
        {
            _mrv.SetException(exception);
        }

        public void CompleteSend()
        {
            _mrv.SetResult(default);
        }

        public bool TryBeginSend()
        {
            // Нотификации не находятся в словаре запросов,
            // поэтому их не может отменить другой поток.
            return true;
        }
    }
}
