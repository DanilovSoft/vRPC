using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class JRpcException : VRpcException
    {
        public JRpcException()
        {
        }

        public JRpcException(string message) : base(message)
        {
        }

        public JRpcException(string message, Exception innerException) : base(message, innerException)
        {
            Debug.Assert(!(innerException is VRpcException));
        }
    }
}
