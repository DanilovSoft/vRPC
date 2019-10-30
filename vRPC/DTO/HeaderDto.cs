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
    internal sealed class HeaderDto
    {
        public const int HeaderMaxSize = 64;
        public static HeaderDto Empty => default;
        private static readonly string HeaderSizeExceededException = $"Размер заголовка сообщения превысил максимально допустимый размер в {HeaderMaxSize} байт.";

        [ProtoMember(1, IsRequired = false)]
        public ushort? Uid { get; }

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

        // Требуется для десериализатора. Если структура то не используется.
        private HeaderDto()
        {

        }

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
        public static HeaderDto CreateRequest(ushort? uid, int contentLength)
        {
            return new HeaderDto(uid, StatusCode.Request, contentLength, null);
        }

        /// <summary>
        /// Конструктор заголовка и для ответа и для запроса.
        /// </summary>
        private HeaderDto(ushort? uid, StatusCode responseCode, int contentLength, string contentEncoding)
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
        public Func<ReadOnlyMemory<byte>, Type, object> GetDeserializer()
        {
            switch (ContentEncoding)
            {
                case ProducesProtoBufAttribute.Encoding:
                        return ExtensionMethods.DeserializeProtoBuf;
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

        /// <summary>
        /// Используется только для отладки и логирования.
        /// </summary>
        public override string ToString()
        {
            return $@"{nameof(Uid)} = {Uid}
{nameof(StatusCode)} = {StatusCode}
{nameof(ContentLength)} = {ContentLength}
{nameof(ContentEncoding)} = {ContentEncoding}";
        }
    }
}
