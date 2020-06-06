using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class ConnectedEventArgs : EventArgs
    {
        public ClientSideConnection Connection { get; }

        [DebuggerStepThrough]
        public ConnectedEventArgs(ClientSideConnection connection)
        {
            Connection = connection;
        }
    }
}
