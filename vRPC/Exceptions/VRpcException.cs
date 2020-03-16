using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class VRpcException : Exception
    {
        public VRpcErrorCode ErrorCode { get; }

        public VRpcException()
        {
        }

        public VRpcException(string message) : base(message)
        {
        }

        public VRpcException(string message, VRpcErrorCode errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public VRpcException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public VRpcException(string message, Exception innerException, VRpcErrorCode errorCode) : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        // Constructor should be protected for unsealed classes, private for sealed classes.
        // (The Serializer invokes this constructor through reflection, so it can be private)
        private VRpcException(SerializationInfo serializationInfo, StreamingContext streamingContext) : base(serializationInfo, streamingContext)
        {
            ErrorCode = (VRpcErrorCode)serializationInfo.GetValue(nameof(ErrorCode), typeof(VRpcErrorCode));
        }

        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(ErrorCode), this.ErrorCode);
            base.GetObjectData(info, context);
        }
    }

    public enum VRpcErrorCode
    {
        None,
        InvalidInterfaceName,
        ConnectionError,
    }
}
