using System;

namespace vRPC
{
    [Serializable]
    public class ProtocolErrorException : Exception
    {
        public ProtocolErrorException()
        {

        }

        public ProtocolErrorException(string message) : base(message)
        {

        }

        public ProtocolErrorException(string message, Exception innerException) : base(message, innerException)
        {

        }
    }
}
