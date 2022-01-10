using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{DebugDisplay}")]
    internal readonly struct InnerConnectionResult
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private ConnectResult DebugDisplay => this.ToPublicConnectResult();
        public SocketError? SocketError { get; }
        public ClientSideConnection? Connection { get; }
        public ShutdownRequest? ShutdownRequest { get; }
        public bool NewConnectionCreated { get; }

        private InnerConnectionResult(ClientSideConnection connection, bool newConnectionCreated)
        {
            Debug.Assert(connection != null);

            Connection = connection;
            SocketError = null;
            ShutdownRequest = null;
            NewConnectionCreated = newConnectionCreated;
        }

        /// <summary>
        /// Подключение не удалось (SocketError может быть Success).
        /// </summary>
        private InnerConnectionResult(SocketError socketError)
        {
            SocketError = socketError;
            ShutdownRequest = null;
            Connection = null;
            NewConnectionCreated = false;
        }

        private InnerConnectionResult(ShutdownRequest shutdownRequest)
        {
            Debug.Assert(shutdownRequest != null);

            ShutdownRequest = shutdownRequest;
            SocketError = null;
            Connection = null;
            NewConnectionCreated = false;
        }

        public static InnerConnectionResult FromExistingConnection(ClientSideConnection connection)
        {
            return new InnerConnectionResult(connection, newConnectionCreated: false);
        }

        public static InnerConnectionResult FromConnectionError(SocketError socketError)
        {
            return new InnerConnectionResult(socketError);
        }

        /// <remarks>Не бросает исключения.</remarks>
        public static InnerConnectionResult FromShutdownRequest(ShutdownRequest shutdownRequest)
        {
            return new InnerConnectionResult(shutdownRequest);
        }

        public static InnerConnectionResult FromNewConnection(ClientSideConnection connection)
        {
            return new InnerConnectionResult(connection, newConnectionCreated: true);
        }

        /// <summary>
        /// Может бросить исключение.
        /// </summary>
        /// <exception cref="VRpcSocketException"/>
        /// <exception cref="VRpcShutdownException"/>
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
                ThrowHelper.ThrowException(SocketError.Value.ToException());
                return default;
            }
            else
            // Пользователь запросил остановку.
            {
                Debug.Assert(ShutdownRequest != null);
                ThrowHelper.ThrowWasShutdownException(ShutdownRequest);
                return default;
            }
        }

        /// <summary>
        /// Может бросить исключение.
        /// </summary>
        /// <exception cref="VRpcSocketException"/>
        /// <exception cref="VRpcShutdownException"/>
        public ValueTask<ClientSideConnection> ToManagedConnectionTask()
        {
            if (Connection != null)
            // Успешно подключились.
            {
                return new(Connection);
            }
            else if (SocketError != null)
            // Не удалось подключиться.
            {
                return new(Task.FromException<ClientSideConnection>(SocketError.Value.ToException()));
            }
            else
            // Пользователь запросил остановку.
            {
                Debug.Assert(ShutdownRequest != null);
                return new(Task.FromException<ClientSideConnection>(new VRpcShutdownException(ShutdownRequest)));
            }
        }
    }
}
