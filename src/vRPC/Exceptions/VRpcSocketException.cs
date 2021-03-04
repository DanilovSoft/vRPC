namespace DanilovSoft.vRPC
{
    using System;
    using System.Collections.Generic;
    using System.Text;

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
