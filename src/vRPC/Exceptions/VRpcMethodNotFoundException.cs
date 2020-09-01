using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class VRpcMethodNotFoundException : VRpcException
    {
        public VRpcMethodNotFoundException()
        {
        }

        public VRpcMethodNotFoundException(string? message) : base(message)
        {
        }

        public VRpcMethodNotFoundException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
