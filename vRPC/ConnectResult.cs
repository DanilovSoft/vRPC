using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace DanilovSoft.vRPC
{
    public readonly struct ConnectResult : IEquatable<ConnectResult>
    {
        public ConnectState State { get; }
        public SocketError? SocketError { get; }

        [DebuggerStepThrough]
        internal ConnectResult(ConnectState connectState, SocketError? socketError)
        {
            State = connectState;
            SocketError = socketError;
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
