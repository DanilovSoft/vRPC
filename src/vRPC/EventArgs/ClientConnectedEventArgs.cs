﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public sealed class ClientConnectedEventArgs : EventArgs
    {
        /// <summary>
        /// Подключенный к серверу клиент.
        /// </summary>
        public OldServerSideConnection Connection { get; }

        [DebuggerStepThrough]
        internal ClientConnectedEventArgs(OldServerSideConnection clientContext)
        {
            Connection = clientContext;
        }
    }
}
