using System;
using System.Diagnostics;

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
