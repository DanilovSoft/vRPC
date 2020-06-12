using System;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcProtocolErrorException : VRpcException
    {
        public VRpcProtocolErrorException()
        {

        }

        public VRpcProtocolErrorException(string message) : base(message)
        {

        }

        public VRpcProtocolErrorException(string message, Exception innerException) : base(message, innerException)
        {

        }
#pragma warning disable CA1801
        private VRpcProtocolErrorException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
