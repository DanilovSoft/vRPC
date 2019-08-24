﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    public sealed class ClientDisconnectedEventArgs : EventArgs
    {
        public ClientContext Connection { get; }
        /// <summary>
        /// Причина обрыва соединения.
        /// </summary>
        public Exception ReasonException { get; }

        [DebuggerStepThrough]
        public ClientDisconnectedEventArgs(ClientContext clientContext, Exception exception)
        {
            Connection = clientContext;
            ReasonException = exception;
        }
    }
}
