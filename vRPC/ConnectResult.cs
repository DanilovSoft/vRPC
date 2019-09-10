using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace DanilovSoft.vRPC
{
    public readonly struct ConnectResult
    {
        public ConnectState State { get; }
        public SocketError? SocketError { get; }

        [DebuggerStepThrough]
        internal ConnectResult(ConnectState connectState, SocketError? socketError)
        {
            State = connectState;
            SocketError = socketError;
        }
    }
}
