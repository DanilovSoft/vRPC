using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace vRPC
{
    internal readonly struct ConnectionResult
    {
        public SocketError SocketError { get; }
        public SocketWrapper SocketWrapper { get; }

        [DebuggerStepThrough]
        public ConnectionResult(SocketError socketError, SocketWrapper socketWrapper)
        {
            SocketError = socketError;
            SocketWrapper = socketWrapper;
        }
    }
}
