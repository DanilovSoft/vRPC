using System;

namespace vRPC
{
    /// <summary>
    /// Исключение для передачи информации об ошибке удаленному подключению.
    /// Исключение этого типа прозрачно транслируется на удаленное подключение.
    /// </summary>
    [Serializable]
    internal class BadRequestException : Exception
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
    }
}
