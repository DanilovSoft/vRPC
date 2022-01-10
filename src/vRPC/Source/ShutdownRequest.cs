using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [DebuggerTypeProxy(typeof(DebugProxy))]
    [DebuggerDisplay(@"\{{ShutdownTimeout}, {CloseDescription}\}")]
    public sealed class ShutdownRequest
    {
        private readonly TaskCompletionSource<CloseReason> _tcs;
        /// <summary>
        /// Причина остановки сервиса указанная пользователем. Может быть <see langword="null"/>.
        /// </summary>
        public string? CloseDescription { get; }
        internal Task<CloseReason> Task => _tcs.Task;
        /// <summary>
        /// Максимальное время ожидания остановки сервиса указанное пользователем 
        /// после которого все соединения закрываются принудительно.
        /// </summary>
        public TimeSpan ShutdownTimeout { get; }

        public ShutdownRequest(TimeSpan disconnectTimeout, string? closeDescription)
        {
            _tcs = new TaskCompletionSource<CloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
            ShutdownTimeout = disconnectTimeout;
            CloseDescription = closeDescription;
        }

        internal void SetTaskResult(CloseReason closeReason)
        {
            _tcs.TrySetResult(closeReason);
        }

        private sealed class DebugProxy
        {
            private readonly ShutdownRequest _self;
            public string? CloseDescription => _self.CloseDescription;
            public TimeSpan ShutdownTimeout => _self.ShutdownTimeout;

            public DebugProxy(ShutdownRequest self)
            {
                _self = self;
            }
        }
    }
}
