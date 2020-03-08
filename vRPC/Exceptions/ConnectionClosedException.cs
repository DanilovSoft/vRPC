using System;
using System.Net.WebSockets;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Исключение которое происходит при грациозном закрытии соединения одной из сторон.
    /// </summary>
    [Serializable]
    public sealed class ConnectionClosedException : Exception
    {
        // "Удалённая сторона закрыла соединение без объяснения причины."
        internal const string ConnectionClosedNormallyMessage = "Произошло грациозное разъединение без указания причины.";
        internal const string ConnectionClosedMessage = "Соединение не установлено.";

        public ConnectionClosedException() : base(ConnectionClosedMessage)
        {
            
        }

        internal ConnectionClosedException(string сloseDescription) : base(сloseDescription)
        {
            
        }

        public ConnectionClosedException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private ConnectionClosedException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
