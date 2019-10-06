using System;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class RpcProtocolErrorException : Exception
    {
        public RpcProtocolErrorException()
        {

        }

        public RpcProtocolErrorException(string message) : base(message)
        {

        }

        public RpcProtocolErrorException(string message, Exception innerException) : base(message, innerException)
        {

        }
#pragma warning disable CA1801
        private RpcProtocolErrorException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
