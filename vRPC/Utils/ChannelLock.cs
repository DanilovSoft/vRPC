// Vitalii Danilov
// Version 1.0.0

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Source;

namespace System.Threading
{
    [DebuggerDisplay(@"\{{_channel.Writer}\}")]
    internal sealed class ChannelLock
    {
        private readonly Channel<VoidStruct> _channel;
        private readonly Releaser _releaser;
        private readonly ValueTask<Releaser> _releaserTask;

        public ChannelLock()
        {
            _channel = Channel.CreateUnbounded<VoidStruct>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = false,
                SingleWriter = true,
            });

            _channel.Writer.TryWrite(VoidStruct.Instance);
            _releaser = new Releaser(this);
            _releaserTask = new ValueTask<Releaser>(_releaser);
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

        /// <summary>
        /// Не бросает исключения.
        /// </summary>
        public ValueTask<Releaser> LockAsync()
        {
            ValueTask<VoidStruct> t = _channel.Reader.ReadAsync();
            if (t.IsCompletedSuccessfully)
            // Успешно захватили блокировку.
            {
                return _releaserTask;
            }
            else
            {
                return WaitForLockAsync(t);
            }
        }

        private async ValueTask<Releaser> WaitForLockAsync(ValueTask<VoidStruct> t)
        {
            await t.ConfigureAwait(false);
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
                _self._channel.Writer.TryWrite(VoidStruct.Instance);
            }
        }
    }
}
