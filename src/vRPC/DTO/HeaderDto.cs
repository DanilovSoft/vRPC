using DanilovSoft.vRPC.Source;
using ProtoBuf;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
        internal static readonly JsonEncodedText JsonUid = JsonEncodedText.Encode("uid");
        internal static readonly JsonEncodedText JsonCode = JsonEncodedText.Encode("code");
        internal static readonly JsonEncodedText JsonPayload = JsonEncodedText.Encode("payload");
        internal static readonly JsonEncodedText JsonEncoding = JsonEncodedText.Encode("encoding");
        internal static readonly JsonEncodedText JsonMethod = JsonEncodedText.Encode("method");

        public const int HeaderMaxSize = 256;
        private static readonly string HeaderSizeExceededException = $"Размер заголовка сообщения превысил максимально допустимый размер в {HeaderMaxSize} байт.";

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
        /// true если задан <see cref="Id"/>.
        /// </summary>
        [JsonIgnore]
        [ProtoIgnore]
        public bool IsResponseRequired => Id != null;

        /// <summary>
        /// Это заголовок запроса когда статус равен <see cref="StatusCode.Request"/>.
        /// </summary>
        [JsonIgnore]
        [ProtoIgnore]
        public bool IsRequest => StatusCode == StatusCode.Request;

        [JsonPropertyName("code")]
        [ProtoMember(1, IsRequired = true)]
        public StatusCode StatusCode { get; }

        [JsonPropertyName("id")]
        [ProtoMember(2, IsRequired = false)]
        public int? Id { get; }

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

        /// <summary>
        /// Конструктор запроса.
        /// </summary>
        /// <param name="id">Может быть Null если запрос является нотификацией.</param>
        public HeaderDto(int? id, int payloadLength, string? contentEncoding, string actionName)
        {
            Id = id;
            StatusCode = StatusCode.Request;
            PayloadLength = payloadLength;
            PayloadEncoding = contentEncoding;
            MethodName = actionName;
        }

        /// <summary>
        /// Конструктор ответа на запрос.
        /// </summary>
        public HeaderDto(int id, int payloadLength, string? contentEncoding, StatusCode responseCode)
        {
            Id = id;
            StatusCode = responseCode;
            PayloadLength = payloadLength;
            PayloadEncoding = contentEncoding;
            MethodName = null;
        }

        /// <summary>
        /// Конструктор сериализатора, для ответа и для запроса.
        /// </summary>
        public HeaderDto(int? uid, StatusCode responseCode, int payloadLength, string? contentEncoding, string? actionName)
        {
            Id = uid;
            StatusCode = responseCode;
            PayloadLength = payloadLength;
            PayloadEncoding = contentEncoding;
            MethodName = actionName;
        }

        /// <summary>
        /// Сериализует заголовок.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="headerSize"></param>
        public void SerializeProtoBuf(ArrayBufferWriter<byte> buffer, out int headerSize)
        {
            int initialPos = buffer.WrittenCount;

            // Сериализуем хедэр.
            ProtoBufSerializer.Serialize(buffer, this);

            headerSize = buffer.WrittenCount - initialPos;

            Debug.Assert(headerSize <= HeaderMaxSize);

            if (headerSize <= HeaderMaxSize)
                return;
            else
                ThrowHelper.ThrowVRpcException(HeaderSizeExceededException);
        }

        /// <summary>
        /// Сериализует заголовок.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        public int SerializeJson(ArrayBufferWriter<byte> bufferWriter)
        {
            int initialPosition = bufferWriter.WrittenCount;

            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                writer.WriteStartObject();

                writer.WriteNumber(JsonCode, (int)StatusCode);

                if (Id != null)
                {
                    writer.WriteNumber(JsonUid, Id.Value);
                }

                writer.WriteNumber(JsonPayload, PayloadLength);

                if (PayloadEncoding != null)
                {
                    writer.WriteString(JsonEncoding, PayloadEncoding);
                }

                if (MethodName != null)
                {
                    writer.WriteString(JsonMethod, MethodName);
                }
                writer.WriteEndObject();
            }

            return bufferWriter.WrittenCount - initialPosition;
        }

        /// <returns>Может быть Null если не удалось десериализовать.</returns>
        public static HeaderDto DeserializeProtoBuf(byte[] buffer, int offset, int count)
        {
            HeaderDto header;
            using (var mem = new MemoryStream(buffer, offset, count))
                header = ProtoBufSerializer.Deserialize<HeaderDto>(mem);
            
            header.Assert();
            return header;
        }

        [Conditional("DEBUG")]
        internal void Assert()
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
            string s = $"Uid = {Id} Status = {StatusCode} Content = {PayloadLength} байт";
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
