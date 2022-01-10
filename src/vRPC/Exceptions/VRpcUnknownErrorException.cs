using System;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcUnknownErrorException : VRpcException
    {
        public int Code { get; }

        public VRpcUnknownErrorException()
        {
        }

        public VRpcUnknownErrorException(int code, string? message) : base(message)
        {
            Code = code;
        }

        public VRpcUnknownErrorException(string? message) : base(message)
        {
        }

        public VRpcUnknownErrorException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
