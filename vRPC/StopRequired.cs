using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    internal sealed class StopRequired
    {
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public string CloseDescription { get; }
        public Task<bool> Task => _tcs.Task;
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
        public bool SetTaskAndReturn(bool gracefully)
        {
            _tcs.TrySetResult(gracefully);
            return gracefully;
        }
    }
}
