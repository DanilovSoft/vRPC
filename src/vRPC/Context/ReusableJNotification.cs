using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace DanilovSoft.vRPC
{
    internal sealed class ReusableJNotification : INotification
    {
        public RequestMethodMeta? Method { get; private set; }
        public object[]? Args { get; private set; }

        public ReusableJNotification()
        {

        }

        internal void Initialize(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(method.IsJsonRpc);
            Debug.Assert(method.IsNotificationRequest);

            Method = method;
            Args = args;
        }

        public ValueTask WaitNotificationAsync()
        {
            Debug.Assert(false);
            throw new NotImplementedException();

            //if (_continuation == s_completedSentinel)
            //// Операция уже завершена.
            //{
            //    ResetTaskState();
            //    return default;
            //}
            //else
            //{
            //    return new ValueTask(this, _token);
            //}
        }

        public bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer, out int headerSize)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }

        public void CompleteNotification(VRpcException exception)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }

        public void CompleteNotification()
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
