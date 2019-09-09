using DanilovSoft;
using DanilovSoft.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace vRPC
{
    internal readonly struct ConnectionResult
    {
        public SocketError? SocketError { get; }
        //public ConnectState State { get; }
        public ManagedConnection Connection { get; }
        public StopRequired StopState { get; }

        [DebuggerStepThrough]
        public ConnectionResult(SocketError? socketError, StopRequired stopState, ManagedConnection context)
        {
            SocketError = socketError;
            Connection = context;
            StopState = stopState;
        }
    }
}
