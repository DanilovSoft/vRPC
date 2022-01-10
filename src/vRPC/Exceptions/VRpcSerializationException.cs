using System;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class VRpcSerializationException : VRpcException
    {
        public VRpcSerializationException()
        {
        }

        public VRpcSerializationException(string? message) : base(message)
        {
        }

        public VRpcSerializationException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
