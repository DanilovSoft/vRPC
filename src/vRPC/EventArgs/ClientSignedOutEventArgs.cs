using System;
using System.Diagnostics;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public sealed class ClientSignedOutEventArgs : EventArgs
    {
        public OldServerSideConnection Connection { get; }
        public ClaimsPrincipal User { get; }
        public VRpcListener Listener => Connection.Listener;

        [DebuggerStepThrough]
        internal ClientSignedOutEventArgs(OldServerSideConnection connection, ClaimsPrincipal user)
        {
            Connection = connection;
            User = user;
        }
    }
}