using System;
using System.Net.WebSockets;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Исключение которое происходит при грациозном закрытии соединения одной из сторон.
    /// </summary>
    [Serializable]
    public sealed class VRpcConnectionClosedException : VRpcException
    {
        // "Удалённая сторона закрыла соединение без объяснения причины."
        internal const string ConnectionClosedNormallyMessage = "Произошло грациозное разъединение без указания причины.";

        public VRpcConnectionClosedException() : base(ConnectionClosedNormallyMessage)
        {
            
        }

        internal VRpcConnectionClosedException(string сloseDescription) : base(сloseDescription)
        {
            
        }

        public VRpcConnectionClosedException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private VRpcConnectionClosedException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
