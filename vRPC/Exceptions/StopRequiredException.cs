using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    [Serializable]
    public class StopRequiredException : Exception
    {
        public StopRequiredException() : base("Stop request has occurred.")
        {

        }

        public StopRequiredException(string message) : base(message)
        {

        }
    }
}
