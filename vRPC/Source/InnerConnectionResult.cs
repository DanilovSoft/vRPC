using DanilovSoft;
using DanilovSoft.WebSockets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{DebugDisplay}")]
    internal readonly struct InnerConnectionResult
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private ConnectResult DebugDisplay => this.ToConnectResult();
        public SocketError? SocketError { get; }
        public ClientSideConnection Connection { get; }
        public ShutdownRequest ShutdownRequest { get; }
        public bool NewConnectionCreated { get; }

        [DebuggerStepThrough]
        public InnerConnectionResult(SocketError? socketError, ShutdownRequest stopRequired, ClientSideConnection connection, bool newConnectionCreated)
        {
            SocketError = socketError;
            Connection = connection;
            ShutdownRequest = stopRequired;
            NewConnectionCreated = newConnectionCreated;
        }

        /// <summary>
        /// Может бросить исключение.
        /// </summary>
        /// <exception cref="SocketException"/>
        /// <exception cref="WasShutdownException"/>
        public ClientSideConnection ToManagedConnection()
        {
            if (Connection != null)
            // Успешно подключились.
            {
                return Connection;
            }
            else if (SocketError != null)
            // Не удалось подключиться.
            {
                throw SocketError.Value.ToException();
            }
            else
            // Пользователь запросил остановку.
            {
                Debug.Assert(ShutdownRequest != null);
                throw new WasShutdownException(ShutdownRequest);
            }
        }

        /// <summary>
        /// Может бросить исключение.
        /// </summary>
        /// <exception cref="SocketException"/>
        /// <exception cref="WasShutdownException"/>
        public ValueTask<ClientSideConnection> ToManagedConnectionTask()
        {
            if (Connection != null)
            // Успешно подключились.
            {
                return new ValueTask<ClientSideConnection>(Connection);
            }
            else if (SocketError != null)
            // Не удалось подключиться.
            {
                return new ValueTask<ClientSideConnection>(Task.FromException<ClientSideConnection>(SocketError.Value.ToException()));
            }
            else
            // Пользователь запросил остановку.
            {
                Debug.Assert(ShutdownRequest != null);
                return new ValueTask<ClientSideConnection>(Task.FromException<ClientSideConnection>(new WasShutdownException(ShutdownRequest)));
            }
        }
    }
}
