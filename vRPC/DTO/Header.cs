using ProtoBuf;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ProtoBufSerializer = ProtoBuf.Serializer;

namespace vRPC
{
    /// <summary>
    /// Заголовок передаваемого сообщения. Размер заголовка — динамический. Сериализатор всегда ProtoBuf.
    /// </summary>
    [ProtoContract]
    [DebuggerDisplay(@"\{Status = {StatusCode}, Content = {ContentLength} байт\}")]
    internal sealed class Header
    {
        public const int HeaderMaxSize = 64;
        private static readonly string HeaderSizeExceededException = $"Размер заголовка сообщения превысил максимально допустимый размер в {HeaderMaxSize} байт.";

        [ProtoMember(1)]
        public ushort Uid { get; }

        [ProtoMember(2)]
        public StatusCode StatusCode { get; }

        [ProtoMember(3, IsRequired = false)]
        public int ContentLength { get; set; }

        /// <summary>
        /// Может быть <see langword="null"/>.
        /// </summary>
        [ProtoMember(4, IsRequired = false)]
        public string ContentEncoding { get; set; }

        // Для сериализатора.
        private Header()
        {

        }

        public Header(ushort uid, StatusCode statusCode)
        {
            Uid = uid;
            StatusCode = statusCode;
        }

        ///// <summary>
        ///// У заголовка в самом начале записан префикс фиксированного размера.
        ///// </summary>
        ///// <param name="source"></param>
        ///// <param name="headerLength">Размер хэдера с учётом префикса.</param>
        ///// <returns></returns>
        //public static bool TryGetHeaderLength(byte[] buffer, int count, out int headerLength)
        //{
        //    // Заголовок сообщения находится в самом начале.
        //    bool gotHeaderPrefix = ProtoBufSerializer.TryReadLengthPrefix(buffer, 0, count, HeaderLengthPrefix, out headerLength);

        //    headerLength += 4; // С учётом размера префикса Fixed32.

        //    if (headerLength > HeaderMaxSize)
        //        throw new InvalidOperationException(HeaderSizeExceededException);

        //    return gotHeaderPrefix && count >= headerLength;
        //}

        //public void SerializeWithLengthPrefix(Stream destination, out int headerSizeWithPrefix)
        //{
        //    long initialPos = destination.Position;

        //    // Сериализуем хедэр с префикс размером в начале.
        //    ProtoBufSerializer.SerializeWithLengthPrefix(destination, this, HeaderLengthPrefix);

        //    headerSizeWithPrefix = (int)(destination.Position - initialPos);

        //    Debug.Assert(headerSizeWithPrefix <= HeaderMaxSize);
        //    if (headerSizeWithPrefix > HeaderMaxSize)
        //        throw new InvalidOperationException(HeaderSizeExceededException);
        //}

        public void SerializeProtoBuf(Stream destination, out int headerSize)
        {
            long initialPos = destination.Position;

            // Сериализуем хедэр с префикс размером в начале.
            ProtoBufSerializer.Serialize(destination, this);

            headerSize = (int)(destination.Position - initialPos);

            Debug.Assert(headerSize <= HeaderMaxSize);

            if (headerSize <= HeaderMaxSize)
                return;

            throw new InvalidOperationException(HeaderSizeExceededException);
        }

        //public static Header DeserializeWithLengthPrefix(Stream source)
        //{
        //    return ProtoBufSerializer.DeserializeWithLengthPrefix<Header>(source, HeaderLengthPrefix);
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static Header DeserializeProtobuf(byte[] buffer, int offset, int count)
        {
            using (var mem = new MemoryStream(buffer, offset, count))
            {
                Header header = ProtoBufSerializer.Deserialize<Header>(mem);
                if (header != null)
                    return header;
            }
            throw new InvalidOperationException("Результатом десериализации оказался Null.");
        }

        /// <summary>
        /// Возвращает подходящий десериализатор соответственно <see cref="ContentEncoding"/>.
        /// </summary>
        public Func<Stream, Type, object> GetDeserializer()
        {
            switch (ContentEncoding)
            {
                case ProducesProtoBufAttribute.Encoding:
                        return ExtensionMethods.DeserializeProtobuf;
                default:
                    return ExtensionMethods.DeserializeJson;
            }
        }
    }
}
