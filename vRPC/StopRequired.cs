using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay("{DisconnectTimeout}, {CloseDescription}")]
    public sealed class StopRequired
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
        public TimeSpan DisconnectTimeout { get; }

        public StopRequired(TimeSpan disconnectTimeout, string closeDescription)
        {
            DisconnectTimeout = disconnectTimeout;
            CloseDescription = closeDescription;
        }

        /// <summary>
        /// Возвращает переданное значение.
        /// </summary>
        internal CloseReason SetTaskResult(CloseReason closeReason)
        {
            _tcs.TrySetResult(closeReason);
            return closeReason;
        }
    }
}
