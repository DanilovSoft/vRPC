using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class VRpcConnectException : VRpcException
    {
        public VRpcConnectException(string message, Exception innerException) : base(message, innerException)
        {

        }

        public VRpcConnectException()
        {
        }

        public VRpcConnectException(string message) : base(message)
        {
        }

        protected VRpcConnectException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
    }
}
