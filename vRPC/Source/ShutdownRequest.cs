using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay("{ShutdownTimeout}, {CloseDescription}")]
    public sealed class ShutdownRequest
    {
        private readonly TaskCompletionSource<CloseReason> _tcs = new TaskCompletionSource<CloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>
        /// Причина остановки сервиса указанная пользователем. Может быть <see langword="null"/>.
        /// </summary>
        public string CloseDescription { get; }
        internal Task<CloseReason> Task => _tcs.Task;
        /// <summary>
        /// Максимальное время ожидания остановки сервиса указанное пользователем 
        /// после которого все соединения закрываются принудительно.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; }

        public ShutdownRequest(TimeSpan disconnectTimeout, string closeDescription)
        {
            ShutdownTimeout = disconnectTimeout;
            CloseDescription = closeDescription;
        }

        internal void SetTaskResult(CloseReason closeReason)
        {
            _tcs.TrySetResult(closeReason);
        }
    }
}
