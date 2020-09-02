using System;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Исключение для передачи информации об ошибке удаленному подключению.
    /// Исключение этого типа прозрачно транслируется на удаленное подключение.
    /// </summary>
    [Serializable]
    public sealed class VRpcBadRequestException : VRpcException, ISerializable
    {
        internal StatusCode ErrorCode { get; }

        public VRpcBadRequestException()
        {

        }

        public VRpcBadRequestException(string message) : base(message)
        {

        }

        internal VRpcBadRequestException(string message, StatusCode errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public VRpcBadRequestException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private VRpcBadRequestException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {

        }
#pragma warning restore CA1801
    }
}
