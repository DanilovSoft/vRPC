using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    public sealed class ClientDisconnectedEventArgs : EventArgs
    {
        public ServerSideConnection Connection { get; }
        /// <summary>
        /// Причина закрытия соединения.
        /// </summary>
        public CloseReason CloseReason { get; }

        [DebuggerStepThrough]
        public ClientDisconnectedEventArgs(ServerSideConnection clientContext, in CloseReason closeReason)
        {
            Connection = clientContext;
            CloseReason = closeReason;
        }
    }
}
