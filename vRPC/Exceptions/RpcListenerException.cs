using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    [Serializable]
    public class RpcListenerException : Exception
    {
        public RpcListenerException()
        {

        }

        public RpcListenerException(string message) : base(message)
        {

        }
    }
}
