using DanilovSoft.vRPC.Source;
using ProtoBuf;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using ProtoBufSerializer = ProtoBuf.Serializer;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Заголовок запроса или ответа. Бинарный размер — динамический. Сериализуется всегда через ProtoBuf.
    /// </summary>
    [ProtoContract]
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal readonly struct HeaderDto : IEquatable<HeaderDto>
    {
        public const int HeaderMaxSize = 64;
        private const string HeaderSizeExceededException = "Размер заголовка сообщения превысил максимально допустимый размер в 64 байта.";

        [JsonIgnore]
        [ProtoIgnore]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay
        {
            get
            {
                if (StatusCode == StatusCode.Request)
                {
                    return $"'{MethodName}', Content = {PayloadLength} байт";
                }
                else
                {
                    return $"Status = {StatusCode}, Content = {PayloadLength} байт";
                }
            }
        }

        /// <summary>
        /// true если задан <see cref="Uid"/>.
        /// </summary>
        [JsonIgnore]
        [ProtoIgnore]
        public bool IsResponseRequired => Uid != null;

        /// <summary>
        /// Это заголовок запроса когда статус равен <see cref="StatusCode.Request"/>.
        /// </summary>
        [JsonIgnore]
        [ProtoIgnore]
        public bool IsRequest => StatusCode == StatusCode.Request;

        [JsonPropertyName("uid")]
        [ProtoMember(1, IsRequired = false)]
        public int? Uid { get; }

        [JsonPropertyName("code")]
        [ProtoMember(2, IsRequired = true)]
        public StatusCode StatusCode { get; }

        [JsonPropertyName("payload")]
        [ProtoMember(3, IsRequired = false)]
        public int PayloadLength { get; }

        /// <summary>
        /// Формат контента. Может быть <see langword="null"/>, тогда 
        /// следует использовать формат по умолчанию.
        /// </summary>
        [JsonPropertyName("encoding")]
        [ProtoMember(4, IsRequired = false)]
        public string? PayloadEncoding { get; }

        /// <summary>
        /// У запроса всегда должно быть имя метода.
        /// </summary>
        [JsonPropertyName("method")]
        [ProtoMember(5, IsRequired = false)]
        public string? MethodName { get; }

        //Требуется для десериализатора.Если структура то не используется.
        //private HeaderDto()
        //{

        //}

        //public HeaderDto(int? uid, StatusCode statusCode, int payloadLength, string actionName, string? contentEncoding)
        //{
        //    Uid = uid;
        //    StatusCode = statusCode;
        //    PayloadLength = payloadLength;
        //    ActionName = actionName;
        //    ContentEncoding = contentEncoding;
        //}

        /// <summary>
        /// Создаёт заголовок ответа на запрос.
        /// </summary>
        public static HeaderDto FromResponse(int uid, StatusCode responseCode, int contentLength, string? contentEncoding)
        {
            return new HeaderDto(uid, responseCode, contentLength, contentEncoding, actionName: null);
        }

        /// <summary>
        /// Создаёт заголовок для нового запроса.
        /// </summary>
        public static HeaderDto CreateRequest(int? uid, int payloadLength, string? contentEncoding, string actionName)
        {
            return new HeaderDto(uid, StatusCode.Request, payloadLength, contentEncoding, actionName);
        }

        /// <summary>
        /// Конструктор заголовка и для ответа и для запроса.
        /// </summary>
        public HeaderDto(int? uid, StatusCode responseCode, int contentLength, string? contentEncoding, string? actionName)
        {
            Uid = uid;
            StatusCode = responseCode;
            PayloadLength = contentLength;
            PayloadEncoding = contentEncoding;
            MethodName = actionName;
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

        /// <returns>Может быть Null если не удалось десериализовать.</returns>
        public static HeaderDto DeserializeProtoBuf(byte[] buffer, int offset, int count)
        {
            HeaderDto header;
            using (var mem = new MemoryStream(buffer, offset, count))
                header = ProtoBufSerializer.Deserialize<HeaderDto>(mem);
            
            header.ValidateDeserializedHeader();
            return header;
        }

        [Conditional("DEBUG")]
        internal void ValidateDeserializedHeader()
        {
            if (IsRequest)
            {
                Debug.Assert(!string.IsNullOrEmpty(MethodName), "У запроса должно быть имя запрашиваемого метода");
            }
        }

        /// <summary>
        /// Используется только для отладки и логирования.
        /// </summary>
        public override string ToString()
        {
            string s = $"Uid = {Uid} Status = {StatusCode} Content = {PayloadLength} байт";
            if (PayloadEncoding != null)
            {
                s += $" {nameof(PayloadEncoding)} = {PayloadEncoding}";
            }
            if (MethodName != null)
            {
                s += $" '{MethodName}'";
            }
            return s;
        }

        public override int GetHashCode()
        {
            return 0;
        }

        public static bool operator ==(in HeaderDto left, in HeaderDto right)
        {
            return left.StatusCode == right.StatusCode;
        }

        public static bool operator !=(in HeaderDto left, in HeaderDto right)
        {
            return !(left == right);
        }

        public override bool Equals(object? obj)
        {
            return false;
        }

        public bool Equals(HeaderDto other)
        {
            return StatusCode == other.StatusCode;
        }
    }
}
