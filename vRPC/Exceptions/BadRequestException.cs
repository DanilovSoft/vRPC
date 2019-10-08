using System;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Исключение для передачи информации об ошибке удаленному подключению.
    /// Исключение этого типа прозрачно транслируется на удаленное подключение.
    /// </summary>
    [Serializable]
    public sealed class BadRequestException : Exception, ISerializable
    {
        public StatusCode ErrorCode { get; }

        public BadRequestException()
        {

        }

        public BadRequestException(string message) : base(message)
        {

        }

        public BadRequestException(string message, StatusCode errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public BadRequestException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private BadRequestException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {

        }
#pragma warning restore CA1801
    }
}
