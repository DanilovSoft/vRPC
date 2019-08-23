// Vitalii Danilov
// Version 1.2.2

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.IO
{
    public class MemoryPoolStream : MemoryStream, IDisposable
    {
        private static readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
        private readonly bool _clearOnReturn;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value >= 0)
                    _position = (int)value;
                else
                    throw new ArgumentOutOfRangeException("Non-negative number required.");
            }
        }
        public override int Capacity
        {
            get => _arrayBuffer.Length;
            set
            {
                if (value != _arrayBuffer.Length)
                {
                    if (value > _arrayBuffer.Length)
                    {
                        ReDim(value);
                    }
                    else
                        throw new ArgumentOutOfRangeException("Capacity was less than the current size.");
                }
            }

        }
        private byte[] _arrayBuffer = Array.Empty<byte>();
        private int _position;
        /// <summary>
        /// Должен совпадать с _position после операции чтения или записи.
        /// </summary>
        private int _bufferPosition;
        private int _disposed;
        private int _length;

        // ctor.
        public MemoryPoolStream(bool clearOnReturn = false)
        {
            _clearOnReturn = clearOnReturn;
        }

        // ctor.
        /// <param name="capacity">Начальный размер буфера.</param>
        public MemoryPoolStream(int capacity, bool clearOnReturn = false)
        {
            if (capacity > 0)
                _arrayBuffer = _pool.Rent(capacity);

            _clearOnReturn = clearOnReturn;
        }

        /// <summary>
        /// Возвращает массив байт, из которого был создан этот поток.
        /// Не используйте этот массив после любых операций с потоком.
        /// </summary>
        /// <returns></returns>
        public byte[] DangerousGetBuffer()
        {
            ThrowIfDisposed();

            return _arrayBuffer;
        }

        public override void Flush()
        {
            // Ничего флашить не нужно.
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();

            int newPosition = 0;
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
                        newPosition = (int)(_length + offset);
                    }
                    break;
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
            ThrowIfDisposed();

            // Проверить пользовательские параметры на выход за допустимые нормы.
            if (value >= 0 && value <= int.MaxValue)
            {
                int newLength = (int)value;
                int deltaLen = newLength - _length;

                if (deltaLen > 0)
                // Нужно увеличить стрим.
                {
                    if (_arrayBuffer.Length < value)
                    // Нужно увеличить буфер.
                    {
                        // Увеличить буфер до требуемого размера.
                        ReDim(newLength);
                    }

                    // Обнулить часть буфера.
                    Array.Clear(_arrayBuffer, _length, deltaLen);
                }
                else
                // Нужно уменьшить стрим.
                {
                    // Если позиция стрима превышает запрашиваемый размер.
                    if (_position > newLength)
                    {
                        // Сравняеть позицию с размером стрима.
                        _position = newLength;
                    }

                    if (_bufferPosition > newLength)
                    {
                        _bufferPosition = newLength;
                    }
                }
                _length = newLength;
            }
            else
                throw new ArgumentOutOfRangeException(nameof(value), "Stream length must be non-negative and less than 2^31 - 1 - origin.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            // Проверить пользовательские параметры на выход за границы массива.
            if (count <= buffer.Length)
            {
                // Сколько данных осталось в стриме.
                int sizeLeft = _length - _position;
                if (sizeLeft > 0)
                {
                    if (count > sizeLeft)
                        count = sizeLeft;

                    Buffer.BlockCopy(_arrayBuffer, _position, buffer, offset, count);

                    // Увеличить позицию стрима.
                    _position += count;

                    // Курсор буфера совпадает с позицией стрима.
                    _bufferPosition = _position;

                    // Вернуть сколько байт прочитано на самом деле.
                    return count;
                }
                return 0;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Buffer size less than requested count.");
            }
        }

        /// <summary>
        /// Возвращает -1 если достигнут конец потока.
        /// </summary>
        public override int ReadByte()
        {
            ThrowIfDisposed();

            // Сколько данных осталось в стриме.
            int sizeLeft = _length - _position;
            if (sizeLeft > 0)
            {
                byte b = _arrayBuffer[_position];

                // Увеличить позицию стрима.
                _position += 1;

                // Курсор буфера совпадает с позицией стрима.
                _bufferPosition = _position;

                // Вернуть прочитанный байт.
                return b;
            }
            return -1;
        }

        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();

            PrepareAppend(1);

            // Копируем в буффер.
            _arrayBuffer[_position] = value;

            // Увеличить позицию стрима.
            _position += 1;

            // Курсор буфера совпадает с позицией стрима.
            _bufferPosition = _position;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            PrepareAppend(count);

            // Копируем в буффер.
            Buffer.BlockCopy(buffer, offset, _arrayBuffer, _position, count);

            // Увеличить позицию стрима.
            _position += count;

            // Курсор буфера совпадает с позицией стрима.
            _bufferPosition = _position;
        }

        private void PrepareAppend(int count)
        {
            // Сколько нужно места в буфере.
            int requiredSize = _position + count;

            // Влезет ли в буфер?
            if (_arrayBuffer.Length >= requiredSize)
            // В буфере достаточно места для новых данных.
            {
                // Проверить соосность курсора и позиции стрима.
                int deltaPosition = _position - _bufferPosition;

                // Если курсор и позиция стрима соосны.
                if (deltaPosition == 0)
                {
                    // Размер стрима увеличен до требуемого размера.
                    if (_length < requiredSize)
                        _length = requiredSize;
                }
                else
                // Пользователь сместил позицию стрима.
                {
                    // Если позицию стрима сместили правее курсора буфера.
                    if (deltaPosition > 0)
                    {
                        // Сколько байт нужно обнулить.
                        int lengthOverhead = _position - _length;

                        if (lengthOverhead > 0)
                        {
                            // Обнулить правую часть буфера до позиции стрима.
                            Array.Clear(_arrayBuffer, _bufferPosition, length: lengthOverhead);
                        }

                        if (_length < requiredSize)
                        {
                            // Размер стрима теперь увеличен до необходимого.
                            _length = requiredSize;
                        }
                    }
                    else
                    // Позиция стрима меньше курсора буфера.
                    {
                        // На сколько увеличится размер стрима.
                        int deltaLen = deltaPosition + count;

                        // При отрицательном числе размер буфера остается прежним.
                        if (deltaLen > 0)
                        {
                            _length += deltaLen;
                        }
                    }
                }
            }
            else
            // Нужно увеличить буфер.
            {
                // Увеличить размер буфера до необходимого размера.
                ReDim(requiredSize);

                // Если позиция больше чем старый буфер то нужно заполнить нулями разницу.
                int clearLen = _position - _length;
                if (clearLen > 0)
                {
                    // Заполнить нулями до позиции стрима.
                    Array.Clear(_arrayBuffer, _bufferPosition, clearLen);
                }

                // Размер стрима увеличен до требуемого размера.
                _length = requiredSize;
            }
        }

        /// <summary>
        /// Увеличивает размер буфера сохраняя данные.
        /// </summary>
        /// <param name="newSize">Необходимый размер буфера. Новый буфер может быть больше указанного размера.</param>
        private void ReDim(int newSize)
        {
            // Арендовать новый буффер размером больше старого.
            // Буфер может быть не пустой!
            byte[] newArray = _pool.Rent(newSize);

            // Если старый буфер был не пустой.
            if (_length > 0)
            {
                // Скопировать полностью старый массив в новый.
                Buffer.BlockCopy(_arrayBuffer, 0, newArray, 0, count: _length);
            }

            // Если буфер не является Array.Empty<byte>.
            if (_arrayBuffer.Length > 0)
            {
                // Вернуть старый буфер в пул.
                _pool.Return(_arrayBuffer, _clearOnReturn);
            }

            // Ссылка теперь указывает на новый буфер.
            _arrayBuffer = newArray;
        }

        public override byte[] ToArray()
        {
            ThrowIfDisposed();

            var copy = new byte[_length];
            Buffer.BlockCopy(_arrayBuffer, 0, copy, 0, _length);
            return copy;
        }

        /// <exception cref="ObjectDisposedException"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed == 0)
                return;

            throw new ObjectDisposedException(GetType().FullName);
        }

        protected override void Dispose(bool disposing)
        {
            // Защита от повторного освобождения буфера.
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                // Если буфер не является Array.Empty<byte>().
                if (_arrayBuffer.Length > 0)
                    _pool.Return(_arrayBuffer, _clearOnReturn);

                if (disposing)
                    GC.SuppressFinalize(this);
            }
        }

        ~MemoryPoolStream()
        {
            Dispose(false);
        }
    }
}
