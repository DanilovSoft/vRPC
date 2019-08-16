using DanilovSoft.WebSocket;
using System;
using System.Threading;

namespace vRPC
{
    internal sealed class SocketWrapper : IDisposable
    {
        private int _state;
        private int _disposed;
        public WebSocket WebSocket { get; }
        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        /// <summary>
        /// Коллекция запросов ожидающие ответ от удалённой стороны.
        /// </summary>
        public RequestQueue RequestCollection { get; }

        public SocketWrapper(WebSocket webSocket)
        {
            WebSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
            RequestCollection = new RequestQueue();
        }

        /// <summary>
        /// Атомарно пытается захватить эксклюзивное право на текущий объект.
        /// </summary>
        public bool TryOwn()
        {
            bool owned = Interlocked.CompareExchange(ref _state, 1, 0) == 0;
            return owned;
        }

        /// <summary>
        /// Атомарно.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                WebSocket.Dispose();
            }
        }
    }
}
