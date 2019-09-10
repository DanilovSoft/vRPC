using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class ClientDisconnectedEventArgs : EventArgs
    {
        public ServerSideConnection Connection { get; }
        /// <summary>
        /// Причина закрытия соединения.
        /// </summary>
        public CloseReason CloseReason { get; }

        [DebuggerStepThrough]
        public ClientDisconnectedEventArgs(ServerSideConnection clientContext, CloseReason closeReason)
        {
            Connection = clientContext;
            CloseReason = closeReason;
        }
    }
}
