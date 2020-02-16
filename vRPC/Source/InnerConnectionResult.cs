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
        public ManagedConnection Connection { get; }
        public ShutdownRequest ShutdownRequest { get; }

        [DebuggerStepThrough]
        public InnerConnectionResult(SocketError? socketError, ShutdownRequest stopRequired, ManagedConnection context)
        {
            SocketError = socketError;
            Connection = context;
            ShutdownRequest = stopRequired;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        public ManagedConnection ToManagedConnection()
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

        /// <exception cref="WasShutdownException"/>
        public ValueTask<ManagedConnection> ToManagedConnectionTask()
        {
            if (Connection != null)
            // Успешно подключились.
            {
                return new ValueTask<ManagedConnection>(Connection);
            }
            else if (SocketError != null)
            // Не удалось подключиться.
            {
                return new ValueTask<ManagedConnection>(Task.FromException<ManagedConnection>(SocketError.Value.ToException()));
            }
            else
            // Пользователь запросил остановку.
            {
                Debug.Assert(ShutdownRequest != null);
                return new ValueTask<ManagedConnection>(Task.FromException<ManagedConnection>(new WasShutdownException(ShutdownRequest)));
            }
        }
    }
}
