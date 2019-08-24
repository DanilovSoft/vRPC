using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    public sealed class SocketDisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Причина обрыва соединения.
        /// </summary>
        public Exception ReasonException { get; }

        [DebuggerStepThrough]
        public SocketDisconnectedEventArgs(Exception reasonException)
        {
            ReasonException = reasonException;
        }
    }
}
