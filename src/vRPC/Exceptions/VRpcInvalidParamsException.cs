using System;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcInvalidParamsException : VRpcException
    {
        public VRpcInvalidParamsException()
        {
        }

        public VRpcInvalidParamsException(string? message) : base(message)
        {
        }

        public VRpcInvalidParamsException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
