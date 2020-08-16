using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class VRpcSerializationException : VRpcException
    {
        public VRpcSerializationException(string message) : base(message)
        {
        }

        public VRpcSerializationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public VRpcSerializationException()
        {
        }
    }
}
