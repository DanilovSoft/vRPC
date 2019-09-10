using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay("{Timeout}, {CloseDescription}")]
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
        /// после которого соединение закрывается принудительно.
        /// </summary>
        public TimeSpan Timeout { get; }

        public StopRequired(TimeSpan timeout, string closeDescription)
        {
            Timeout = timeout;
            CloseDescription = closeDescription;
        }

        /// <summary>
        /// Возвращает переданное значение.
        /// </summary>
        /// <param name="gracefully"></param>
        /// <returns></returns>
        internal CloseReason SetTaskAndReturn(CloseReason closeReason)
        {
            _tcs.TrySetResult(closeReason);
            return closeReason;
        }
    }
}
