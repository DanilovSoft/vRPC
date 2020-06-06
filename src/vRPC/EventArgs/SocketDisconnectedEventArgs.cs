using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class SocketDisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Причина обрыва соединения.
        /// </summary>
        public CloseReason DisconnectReason { get; }
        public ManagedConnection Connection { get; }

        [DebuggerStepThrough]
        public SocketDisconnectedEventArgs(ManagedConnection connection, CloseReason closeResult)
        {
            Connection = connection;
            DisconnectReason = closeResult;
        }
    }
}
