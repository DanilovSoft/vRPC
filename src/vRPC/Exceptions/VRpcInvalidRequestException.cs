using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcInvalidRequestException : VRpcException
    {
        public VRpcInvalidRequestException()
        {

        }

        public VRpcInvalidRequestException(string? message) : base(message)
        {
        }

        public VRpcInvalidRequestException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
