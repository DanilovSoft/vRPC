﻿using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC;

namespace System.IO
{
    /// <summary>Provides a <see cref="Stream"/> for the contents of a <see cref="ReadOnlyMemory{Byte}"/>.</summary>
    internal sealed class ReadOnlyMemoryStream : Stream
    {
        private readonly ReadOnlyMemory<byte> _content;
        private int _position;

        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> content)
        {
            _content = content;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > int.MaxValue)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(nameof(value));
                }
                _position = (int)value;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long pos =
                origin == SeekOrigin.Begin ? offset :
                origin == SeekOrigin.Current ? _position + offset :
                origin == SeekOrigin.End ? _content.Length + offset :
                throw new ArgumentOutOfRangeException(nameof(origin));

            if (pos > int.MaxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(offset));
            }
            else if (pos < 0)
            {
                throw new IOException("SeekBeforeBegin");
            }

            _position = (int)pos;
            return _position;
        }

        public override int ReadByte()
        {
            ReadOnlySpan<byte> s = _content.Span;
            return _position < s.Length ? s[_position++] : -1;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateReadArrayArguments(buffer, offset, count);
            return Read(new Span<byte>(buffer, offset, count));
        }

        public
#if !NETSTANDARD2_0 && !NET472
        override 
#endif
        int Read(Span<byte> buffer)
        {
            int remaining = _content.Length - _position;

            if (remaining <= 0 || buffer.Length == 0)
            {
                return 0;
            }
            else if (remaining <= buffer.Length)
            {
                _content.Span.Slice(_position).CopyTo(buffer);
                _position = _content.Length;
                return remaining;
            }
            else
            {
                _content.Span.Slice(_position, buffer.Length).CopyTo(buffer);
                _position += buffer.Length;
                return buffer.Length;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateReadArrayArguments(buffer, offset, count);
            return cancellationToken.IsCancellationRequested ?
                Task.FromCanceled<int>(cancellationToken) :
                Task.FromResult(Read(new Span<byte>(buffer, offset, count)));
        }

        public
#if !NETSTANDARD2_0 && !NET472
            override 
#endif
            ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            cancellationToken.IsCancellationRequested ?
                new ValueTask<int>(Task.FromCanceled<int>(cancellationToken)) :
                new ValueTask<int>(Read(buffer.Span));

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) =>
            TaskToApm.Begin(ReadAsync(buffer, offset, count), callback, state);

        public override int EndRead(IAsyncResult asyncResult) =>
            TaskToApm.End<int>(asyncResult);


#if !NETSTANDARD2_0 && !NET472
        public override void CopyTo(Stream destination, int bufferSize)
        {
            StreamHelpers.ValidateCopyToArgs(this, destination, bufferSize);
            if (_content.Length > _position)
            {
                destination.Write(_content.Span.Slice(_position));
            }
        }
#endif

#if !NETSTANDARD2_0 && !NET472
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            StreamHelpers.ValidateCopyToArgs(this, destination, bufferSize);
            return _content.Length > _position ?
                destination.WriteAsync(_content.Slice(_position), cancellationToken).AsTask() :
                Task.CompletedTask;
        }
#endif

        public override void Flush() { }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static void ValidateReadArrayArguments(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(buffer));
            }
            if (offset < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0 || buffer.Length - offset < count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(count));
            }
        }
    }
}
