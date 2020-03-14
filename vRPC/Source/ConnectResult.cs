using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{{State}\}")]
    public readonly struct ConnectResult : IEquatable<ConnectResult>
    {
        public ConnectionState State { get; }
        /// <summary>
        /// Может быть <see cref="System.Net.Sockets.SocketError.Success"/> несмотря на то что State == <see cref="ConnectionState.SocketError"/>.
        /// </summary>
        public SocketError? SocketError { get; }
        /// <summary>
        /// Не Null если State = <see cref="ConnectionState.ShutdownRequest"/>.
        /// </summary>
        public ShutdownRequest ShutdownRequest { get; }

        [DebuggerStepThrough]
        private ConnectResult(ConnectionState connectState, SocketError? socketError, ShutdownRequest shutdownRequest)
        {
            State = connectState;
            SocketError = socketError;
            ShutdownRequest = shutdownRequest;
        }

        internal static ConnectResult FromConnectionSuccess()
        {
            return new ConnectResult(ConnectionState.Connected, null, null);
        }

        internal static ConnectResult FromError(SocketError socketError)
        {
            return new ConnectResult(ConnectionState.SocketError, socketError, null);
        }

        internal static ConnectResult FromShutdownRequest(ShutdownRequest shutdownRequest)
        {
            return new ConnectResult(ConnectionState.ShutdownRequest, null, shutdownRequest);
        }

        public bool Equals(ConnectResult other)
        {
            return other.SocketError == SocketError
                    && other.State == State;
        }

        public override bool Equals(object obj)
        {
            if(obj is ConnectResult connectResult)
            {
                return Equals(connectResult);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (State, SocketError).GetHashCode();
        }

        public static bool operator ==(ConnectResult left, ConnectResult right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ConnectResult left, ConnectResult right)
        {
            return !(left == right);
        }
    }
}
