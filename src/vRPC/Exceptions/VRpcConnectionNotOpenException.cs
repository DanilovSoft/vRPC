using System;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Исключение которое происходит при обращении к закрытому соединению.
    /// </summary>
    [Serializable]
    public sealed class VRpcConnectionNotOpenException : VRpcException
    {
        internal const string ConnectionClosedMessage = "Соединение не установлено.";

        public VRpcConnectionNotOpenException() : base(ConnectionClosedMessage)
        {

        }

        internal VRpcConnectionNotOpenException(string сloseDescription) : base(сloseDescription)
        {

        }

        public VRpcConnectionNotOpenException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private VRpcConnectionNotOpenException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {

        }
#pragma warning restore CA1801
    }
}
