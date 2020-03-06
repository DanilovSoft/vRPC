using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcException : Exception
    {
        public VRpcException()
        {
        }

        public VRpcException(string message) : base(message)
        {
        }

        public VRpcException(string message, Exception innerException) : base(message, innerException)
        {
        }

        private VRpcException(SerializationInfo serializationInfo, StreamingContext streamingContext) : base(serializationInfo, streamingContext)
        {
            
        }
    }
}
