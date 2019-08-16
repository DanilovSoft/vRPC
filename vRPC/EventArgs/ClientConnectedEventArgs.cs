using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    public sealed class ClientConnectedEventArgs : EventArgs
    {
        public ClientContext Connection { get; }

        [DebuggerStepThrough]
        public ClientConnectedEventArgs(ClientContext clientContext)
        {
            Connection = clientContext;
        }
    }
}
