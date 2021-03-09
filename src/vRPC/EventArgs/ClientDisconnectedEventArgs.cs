using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class ClientDisconnectedEventArgs : EventArgs
    {
        public OldServerSideConnection Connection { get; }
        /// <summary>
        /// Причина закрытия соединения.
        /// </summary>
        public CloseReason CloseReason { get; }
        public VRpcListener Listener => Connection.Listener;

        [DebuggerStepThrough]
        internal ClientDisconnectedEventArgs(OldServerSideConnection clientContext, CloseReason closeReason)
        {
            Connection = clientContext;
            CloseReason = closeReason;
        }
    }
}
