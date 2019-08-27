using DanilovSoft.WebSocket;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace vRPC
{
    /// <summary>
    /// Обвёртка для подключенного веб-сокета.
    /// </summary>
    internal sealed class SocketWrapper : IDisposable
    {
        private int _state;
        private int _disposed;
        /// <summary>
        /// Объект можно использовать только для просмотра состояния.
        /// </summary>
        public ManagedWebSocket WebSocket { get; }
        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        /// <summary>
        /// Коллекция запросов ожидающие ответ от удалённой стороны.
        /// </summary>
        public RequestQueue PendingRequests { get; }

        public SocketWrapper(ManagedWebSocket webSocket)
        {
            WebSocket = webSocket;
            PendingRequests = new RequestQueue();
        }

        /// <summary>
        /// Атомарно захватывает эксклюзивное право на текущий объект.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryOwn()
        {
            bool owned = Interlocked.CompareExchange(ref _state, 1, 0) == 0;
            return owned;
        }

        /// <summary>
        /// Атомарно освобождает ресурсы <see cref="WebSocket"/>.
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
