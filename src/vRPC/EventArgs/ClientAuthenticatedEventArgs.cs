using System;
using System.Diagnostics;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public sealed class ClientAuthenticatedEventArgs : EventArgs
    {
        public OldServerSideConnection Connection { get; }
        public ClaimsPrincipal User { get; }
        public VRpcListener Listener => Connection.Listener;

        [DebuggerStepThrough]
        internal ClientAuthenticatedEventArgs(OldServerSideConnection connection, ClaimsPrincipal user)
        {
            Connection = connection;
            User = user;
        }
    }
}