using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    public sealed class ClientConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Подключенный к серверу клиент.
        /// </summary>
        public ServerSideConnection Connection { get; }

        [DebuggerStepThrough]
        internal ClientConnectedEventArgs(ServerSideConnection clientContext)
        {
            Connection = clientContext;
        }
    }
}
