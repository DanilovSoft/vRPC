using System;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcSocketException : VRpcException
    {
        public VRpcSocketException(string message) : base(message)
        {
        }

        public VRpcSocketException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
