using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class VRpcException : Exception
    {
        public VRpcException()
        {
        }

        public VRpcException(string message) : base(message)
        {
        }

        public VRpcException(string message, Exception innerException) : base(message, innerException)
        {
            Debug.Assert(!(innerException is VRpcException));
        }

        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        // Constructor should be protected for unsealed classes, private for sealed classes.
        // (The Serializer invokes this constructor through reflection, so it can be private)
        protected VRpcException(SerializationInfo serializationInfo, StreamingContext streamingContext) : base(serializationInfo, streamingContext)
        {
            
        }
    }
}
