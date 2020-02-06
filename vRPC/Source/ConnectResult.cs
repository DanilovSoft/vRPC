using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{{State}\}")]
    public readonly struct ConnectResult : IEquatable<ConnectResult>
    {
        public ConnectionState State { get; }
        public SocketError? SocketError { get; }
        /// <summary>
        /// Не Null если State = <see cref="ConnectionState.ShutdownRequest"/>.
        /// </summary>
        public ShutdownRequest ShutdownRequest { get; }

        [DebuggerStepThrough]
        internal ConnectResult(ConnectionState connectState, SocketError? socketError, ShutdownRequest shutdownRequest)
        {
            State = connectState;
            SocketError = socketError;
            ShutdownRequest = shutdownRequest;
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
