using System;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcInternalErrorException : VRpcException
    {
        public VRpcInternalErrorException()
        {
        }

        public VRpcInternalErrorException(string? message) : base(message)
        {
        }

        public VRpcInternalErrorException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
