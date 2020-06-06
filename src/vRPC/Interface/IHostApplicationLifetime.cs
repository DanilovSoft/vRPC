using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    public interface IHostApplicationLifetime
    {
        /// <summary>
        /// Triggered when the application host has fully started.
        /// </summary>
        public CancellationToken ApplicationStarted { get; }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown. Shutdown will block until this event completes.
        /// </summary>
        public CancellationToken ApplicationStopping { get; }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown. Shutdown will block until this event completes.
        /// </summary>
        public CancellationToken ApplicationStopped { get; }
    }
}
