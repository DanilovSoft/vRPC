using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Исключение которое происходит при обращении к закрытому соединению.
    /// </summary>
    [Serializable]
    public sealed class ConnectionNotOpenException : Exception
    {
        internal const string ConnectionClosedMessage = "Соединение не установлено.";

        public ConnectionNotOpenException() : base(ConnectionClosedMessage)
        {

        }

        internal ConnectionNotOpenException(string сloseDescription) : base(сloseDescription)
        {

        }

        public ConnectionNotOpenException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private ConnectionNotOpenException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {

        }
#pragma warning restore CA1801
    }
}
