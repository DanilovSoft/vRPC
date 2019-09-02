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
        public ReceiveResult ReceiveResult { get; }
        public ManagedConnection Context { get; }

        [DebuggerStepThrough]
        public ConnectionResult(in ReceiveResult receiveResult, ManagedConnection context)
        {
            ReceiveResult = receiveResult;
            Context = context;
        }
    }
}
