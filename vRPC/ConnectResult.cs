using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace vRPC
{
    public readonly struct ConnectResult
    {
        public bool Success { get; }
        public SocketError SocketError { get; }

        [DebuggerStepThrough]
        internal ConnectResult(bool success, SocketError socketError)
        {
            Success = success;
            SocketError = socketError;
        }
    }
}
