using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class JNotification : INotification
    {
        public RequestMethodMeta Method { get; }
        public object?[] Args { get; }
        public bool IsNotification => true;

        public JNotification(RequestMethodMeta method, object?[] args)
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
        }

        //public bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize)
        //{
        //    Debug.Assert(false);
        //    throw new NotImplementedException();
        //}

        //public void CompleteNotification(VRpcException exception)
        //{
        //    Debug.Assert(false);
        //    throw new NotImplementedException();
        //}

        //public void CompleteNotification()
        //{
        //    Debug.Assert(false);
        //    throw new NotImplementedException();
        //}
    }
}
