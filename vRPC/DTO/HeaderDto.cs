using ProtoBuf;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ProtoBufSerializer = ProtoBuf.Serializer;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Заголовок передаваемого сообщения. Размер заголовка — динамический. Сериализатор всегда ProtoBuf.
    /// </summary>
    [ProtoContract]
    [DebuggerDisplay(@"\{Status = {StatusCode}, Content = {ContentLength} байт\}")]
    internal readonly struct HeaderDto
    {
        public const int HeaderMaxSize = 64;
        public static readonly HeaderDto Empty = default;
        private static readonly string HeaderSizeExceededException = $"Размер заголовка сообщения превысил максимально допустимый размер в {HeaderMaxSize} байт.";

        [ProtoMember(1)]
        public ushort Uid { get; }

        [ProtoMember(2)]
        public StatusCode StatusCode { get; }

        [ProtoMember(3, IsRequired = false)]
        public int ContentLength { get; }

        /// <summary>
        /// Формат контента. Может быть <see langword="null"/>, тогда 
        /// следует использовать формат по умолчанию.
        /// </summary>
        [ProtoMember(4, IsRequired = false)]
        public string ContentEncoding { get; }

        /// <summary>
        /// Создаёт заголовок ответа на запрос.
        /// </summary>
        public static HeaderDto FromResponse(ushort uid, StatusCode responseCode, int contentLength, string contentEncoding)
        {
            return new HeaderDto(uid, responseCode, contentLength, contentEncoding);
        }

        /// <summary>
        /// Создаёт заголовок для нового запроса.
        /// </summary>
        public static HeaderDto CreateRequest(ushort uid, int contentLength)
        {
            return new HeaderDto(uid, StatusCode.Request, contentLength, null);
        }

        /// <summary>
        /// Конструктор заголовка и для ответа и для запроса.
        /// </summary>
        private HeaderDto(ushort uid, StatusCode responseCode, int contentLength, string contentEncoding)
        {
            Uid = uid;
            StatusCode = responseCode;
            ContentLength = contentLength;
            ContentEncoding = contentEncoding;
        }

        /// <summary>
        /// Сериализует заголовок. Не должно бросать исключения(!).
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="headerSize"></param>
        public void SerializeProtoBuf(Stream stream, out int headerSize)
        {
            int initialPos = (int)stream.Position;

            // Сериализуем хедэр.
            ProtoBufSerializer.Serialize(stream, this);

            headerSize = (int)stream.Position - initialPos;

            Debug.Assert(headerSize <= HeaderMaxSize);

            if (headerSize <= HeaderMaxSize)
                return;

            throw new ApplicationException(HeaderSizeExceededException);
        }

        /// <summary>
        /// Может вернуть <see langword="null"/> если не удалось десериализовать.
        /// </summary>
        public static HeaderDto DeserializeProtobuf(byte[] buffer, int offset, int count)
        {
            using (var mem = new MemoryStream(buffer, offset, count))
            {
                HeaderDto header = ProtoBufSerializer.Deserialize<HeaderDto>(mem);
                return header; // может быть null если не удалось десериализовать.
            }
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
                    return ExtensionMethods.DeserializeJson; // Сериализатор по умолчанию.
            }
        }

        //public static bool operator ==(in HeaderDto a, in HeaderDto b)
        //{
        //    return a.Equals(b);
        //}

        //public static bool operator !=(in HeaderDto a, in HeaderDto b)
        //{
        //    return !a.Equals(b);
        //}
    }
}
