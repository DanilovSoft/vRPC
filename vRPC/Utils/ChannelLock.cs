// Vitalii Danilov
// Version 1.0.0

using System;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace System.Threading
{
    [DebuggerDisplay(@"\{{_channel.Writer}\}")]
    internal sealed class ChannelLock
    {
        private readonly Channel<int> _channel;
        private readonly Releaser _releaser;

        public ChannelLock()
        {
            _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = false,
                SingleWriter = true,
            });

            _channel.Writer.TryWrite(0);
            _releaser = new Releaser(this);
        }

        public bool TryLock(out Releaser releaser)
        {
            if(_channel.Reader.TryRead(out _))
            {
                releaser = _releaser;
                return true;
            }
            releaser = default;
            return false;
        }

        public async ValueTask<Releaser> LockAsync()
        {
            await _channel.Reader.ReadAsync().ConfigureAwait(false);
            // Успешно захватили блокировку.

            return _releaser;
        }

        internal readonly struct Releaser : IDisposable
        {
            private readonly ChannelLock _self;

            public Releaser(ChannelLock self)
            {
                _self = self;
            }

            public void Dispose()
            {
                _self._channel.Writer.TryWrite(0);
            }
        }
    }
}
