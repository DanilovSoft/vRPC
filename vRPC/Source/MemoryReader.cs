using System;
using System.Collections.Generic;
using System.Text;

namespace System.IO
{
    internal sealed class MemoryReader : Stream
    {
        private readonly ReadOnlyMemory<byte> _memory;
        private int _position;

        public MemoryReader(ReadOnlyMemory<byte> memory)
        {
            _memory = memory;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _memory.Length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value > _memory.Length)
                    throw new ArgumentOutOfRangeException();

                _position = (int)value;
            }
        }

        public override void Flush()
        {
            
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var destMem = buffer.AsMemory(offset, count);

            int lenLeft = _memory.Length - _position;
            if (count > lenLeft)
                count = lenLeft;

            if (count > 0)
            {
                _memory.Slice(_position, count).CopyTo(destMem);
                _position += count;
                return count;
            }
            return 0;
        }

#if !NETSTANDARD2_0

        public override int Read(Span<byte> buffer)
        {
            int count = buffer.Length;
            int lenLeft = _memory.Length - _position;
            if (count > lenLeft)
                count = lenLeft;

            if (count > 0)
            {
                _memory.Span.Slice(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }
            return 0;
        }
#endif

        public override int ReadByte()
        {
            int lenLeft = _memory.Length - _position;
            if(lenLeft > 0)
            {
                byte b = _memory.Span[_position];
                _position++;
                return b;
            }
            return -1;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            // offset может быть отрицательным ;)

            int newPosition;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    {
                        newPosition = (int)offset;
                    }
                    break;
                case SeekOrigin.Current:
                    {
                        newPosition = (int)(_position + offset);
                    }
                    break;
                case SeekOrigin.End:
                    // Сдвинуть указатель вперед начиная с конца.
                    {
                        newPosition = (int)(_memory.Length + offset);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(origin));
            }

            // Позиция не может быть отрицательной.
            if (newPosition >= 0)
            {
                _position = newPosition;

                // Нужно вернуть итоговую позицию.
                return newPosition;
            }
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
