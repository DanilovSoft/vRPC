// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Этот класс передаётся через интерфейс поэтому лучше не делать структурой.
    /// </summary>
    [DebuggerDisplay(@"\{IsRented = {IsRented}\}")]
    [StructLayout(LayoutKind.Auto)]
    internal sealed class ArrayBufferWriter<T> : IBufferWriter<T>
    {
        public const int MinimumBufferSize = 256;

        private T[]? _rentedBuffer;
        private int _index;
        public bool IsRented => _rentedBuffer != null;

        public ArrayBufferWriter(bool initialize = true)
        {
            if (initialize)
            {
                Rent();
            }
        }

        public void Rent()
        {
            Debug.Assert(_rentedBuffer == null);

            _rentedBuffer = ArrayPool<T>.Shared.Rent(MinimumBufferSize);
            _index = 0;
        }

        public void Return()
        {
            Debug.Assert(_rentedBuffer != null);

            if (_rentedBuffer != null)
            {
                ArrayPool<T>.Shared.Return(_rentedBuffer, clearArray: false);
                _rentedBuffer = null;
            }
        }

        public ReadOnlyMemory<T> WrittenMemory
        {
            get
            {
                CheckIfDisposed();

                return _rentedBuffer.AsMemory(0, _index);
            }
        }

        public int WrittenCount
        {
            get
            {
                CheckIfDisposed();

                return _index;
            }
        }

        public int Capacity
        {
            get
            {
                Debug.Assert(_rentedBuffer != null);

                CheckIfDisposed();

                return _rentedBuffer.Length;
            }
        }

        public int FreeCapacity
        {
            get
            {
                Debug.Assert(_rentedBuffer != null);

                CheckIfDisposed();

                return _rentedBuffer.Length - _index;
            }
        }

        public void Clear()
        {
            CheckIfDisposed();

            ClearHelper();
        }

        private void ClearHelper()
        {
            Debug.Assert(_rentedBuffer != null);

            _rentedBuffer.AsSpan(0, _index).Clear();
            _index = 0;
        }

        private void CheckIfDisposed()
        {
            if (_rentedBuffer != null)
                return;
            
            ThrowHelper.ThrowObjectDisposedException(nameof(ArrayBufferWriter<T>));
        }

        public void Advance(int count)
        {
            Debug.Assert(_rentedBuffer != null);

            CheckIfDisposed();

            if (count < 0)
                ThrowHelper.ThrowArgumentException(nameof(count));

            if (_index > _rentedBuffer.Length - count)
                ThrowHelper.ThrowInvalidOperationException($"Cannot advance past the end of the buffer, which has a size of {_rentedBuffer.Length}.");

            _index += count;
        }

        public Memory<T> GetMemory(int sizeHint = 0)
        {
            CheckIfDisposed();

            CheckAndResizeBuffer(sizeHint);
            return _rentedBuffer.AsMemory(_index);
        }

        public Span<T> GetSpan(int sizeHint = 0)
        {
            CheckIfDisposed();

            CheckAndResizeBuffer(sizeHint);
            return _rentedBuffer.AsSpan(_index);
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            Debug.Assert(_rentedBuffer != null);

            if (sizeHint < 0)
                ThrowHelper.ThrowArgumentException(nameof(sizeHint));

            if (sizeHint == 0)
            {
                sizeHint = MinimumBufferSize;
            }

            int availableSpace = _rentedBuffer.Length - _index;

            if (sizeHint > availableSpace)
            {
                int growBy = Math.Max(sizeHint, _rentedBuffer.Length);

                int newSize = checked(_rentedBuffer.Length + growBy);

                T[] oldBuffer = _rentedBuffer;

                _rentedBuffer = ArrayPool<T>.Shared.Rent(newSize);

                Debug.Assert(oldBuffer.Length >= _index);
                Debug.Assert(_rentedBuffer.Length >= _index);

                Span<T> previousBuffer = oldBuffer.AsSpan(0, _index);
                previousBuffer.CopyTo(_rentedBuffer);
                previousBuffer.Clear();
                ArrayPool<T>.Shared.Return(oldBuffer);
            }

            Debug.Assert(_rentedBuffer.Length - _index > 0);
            Debug.Assert(_rentedBuffer.Length - _index >= sizeHint);
        }

//#if DEBUG
//        ~ArrayBufferWriter()
//        {
//            Debug.Assert(false);
//            throw new NotSupportedException();
//        }
//#endif
    }
}