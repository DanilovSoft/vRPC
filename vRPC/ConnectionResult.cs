using DanilovSoft;
using DanilovSoft.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal readonly struct ConnectionResult
    {
        public SocketError? SocketError { get; }
        public ManagedConnection Connection { get; }
        public StopRequired StopRequired { get; }

        [DebuggerStepThrough]
        public ConnectionResult(SocketError? socketError, StopRequired stopRequired, ManagedConnection context)
        {
            SocketError = socketError;
            Connection = context;
            StopRequired = stopRequired;
        }
    }
}
