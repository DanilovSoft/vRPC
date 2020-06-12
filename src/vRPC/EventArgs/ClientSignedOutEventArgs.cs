using System;
using System.Diagnostics;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public sealed class ClientSignedOutEventArgs : EventArgs
    {
        public ServerSideConnection Connection { get; }
        public ClaimsPrincipal User { get; }
        public VRpcListener Listener => Connection.Listener;

        [DebuggerStepThrough]
        internal ClientSignedOutEventArgs(ServerSideConnection connection, ClaimsPrincipal user)
        {
            Connection = connection;
            User = user;
        }
    }
}