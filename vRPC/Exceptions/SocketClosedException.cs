using System;

namespace vRPC
{
    [Serializable]
    public class SocketClosedException : Exception
    {
        public SocketClosedException()
        {

        }

        public SocketClosedException(string message) : base(message)
        {

        }
    }
}
