using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Length = {Memory.Length}\}")]
    public sealed class RentedMemory : IDisposable
    {
        private IMemoryOwner<byte>? _ownerToDispose;
        public ReadOnlyMemory<byte> Memory { get; private set; }

        internal RentedMemory(IMemoryOwner<byte> ownerToDispose, int length)
        {
            _ownerToDispose = ownerToDispose;
            Memory = ownerToDispose.Memory.Slice(0, length);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _ownerToDispose, null)?.Dispose();
        }
    }
}
