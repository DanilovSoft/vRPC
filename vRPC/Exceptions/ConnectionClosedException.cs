using System;
using System.Net.WebSockets;

namespace vRPC
{
    /// <summary>
    /// Исключение которое происходит при грациозном закрытии соединения одной из сторон.
    /// </summary>
    [Serializable]
    public sealed class ConnectionClosedException : Exception
    {
        public string CloseDescription { get; }

        public ConnectionClosedException()
        {
            
        }

        internal ConnectionClosedException(string сloseDescription) : base(сloseDescription ?? "Произошло грациозное разъединение без указания причины.")
        {
            CloseDescription = сloseDescription;
        }
    }
}
