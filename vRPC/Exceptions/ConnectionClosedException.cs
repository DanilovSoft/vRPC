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
        internal const string DefaultMessage = "Произошло грациозное разъединение без указания причины.";

        public string CloseDescription { get; }

        public ConnectionClosedException()
        {
            
        }

        internal ConnectionClosedException(string сloseDescription) : base(сloseDescription ?? DefaultMessage)
        {
            CloseDescription = сloseDescription;
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
