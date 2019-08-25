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
        /// Причина обрыва соединения.
        /// </summary>
        public Exception ReasonException { get; }

        [DebuggerStepThrough]
        public ClientDisconnectedEventArgs(ServerSideConnection clientContext, Exception exception)
        {
            Connection = clientContext;
            ReasonException = exception;
        }
    }
}
