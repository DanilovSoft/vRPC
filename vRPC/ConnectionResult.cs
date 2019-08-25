﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace vRPC
{
    internal readonly struct ConnectionResult
    {
        public SocketError SocketError { get; }
        public ManagedConnection Context { get; }

        [DebuggerStepThrough]
        public ConnectionResult(SocketError socketError, ManagedConnection context)
        {
            SocketError = socketError;
            Context = context;
        }
    }
}
