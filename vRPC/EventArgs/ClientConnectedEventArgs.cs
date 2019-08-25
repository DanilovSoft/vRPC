using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    public sealed class ClientConnectedEventArgs : EventArgs
    {
        public ServerSideConnection Connection { get; }

        [DebuggerStepThrough]
        public ClientConnectedEventArgs(ServerSideConnection clientContext)
        {
            Connection = clientContext;
        }
    }
}
