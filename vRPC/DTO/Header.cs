using ProtoBuf;
using System;
using System.IO;
using System.Text;
using ProtoBufSerializer = ProtoBuf.Serializer;

namespace vRPC
{
    /// <summary>
    /// Заголовок передаваемого сообщения. Формат заголовка всегда предопределён.
    /// </summary>
    [ProtoContract]
    internal sealed class Header
    {
        private const PrefixStyle HeaderLengthPrefix = PrefixStyle.Fixed32;

        [ProtoMember(1)]
        public short Uid { get; }

        [ProtoMember(2)]
        public StatusCode StatusCode { get; }

        [ProtoMember(3, IsRequired = false)]
        public int ContentLength { get; set; }

        [ProtoMember(4, IsRequired = false)]
        public string ContentEncoding { get; set; }

        // Для сериализатора.
        private Header()
        {

        }

        public Header(short uid, StatusCode statusCode)
        {
            Uid = uid;
            StatusCode = statusCode;
        }

        public static bool TryReadLengthPrefix(Stream source, out int length)
        {
            long pos = source.Position;

            // Заголовок сообщения находится в самом начале.
            source.Position = 0;

            bool success = ProtoBufSerializer.TryReadLengthPrefix(source, HeaderLengthPrefix, out length);

            source.Position = pos;

            return success;
        }

        public void SerializeWithLengthPrefix(Stream destination)
        {
            ProtoBufSerializer.SerializeWithLengthPrefix(destination, this, HeaderLengthPrefix);
        }

        public static Header DeserializeWithLengthPrefix(Stream source)
        {
            return ProtoBufSerializer.DeserializeWithLengthPrefix<Header>(source, HeaderLengthPrefix);
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
