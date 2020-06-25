using ProtoBuf;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DanilovSoft.vRPC
{
    [Serializable] // Разрешим юзеру хранить токен в любом виде.
    [ProtoContract]
    //[JsonConverter(typeof(AccessTokenJsonConverter))]
    [DebuggerDisplay(@"\{Length = {Bytes.Length}, {AsHex}\}")]
    public struct AccessToken : IEquatable<AccessToken>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string AsHex => Convert.ToBase64String(Bytes);

        [JsonPropertyName("Bytes")]
        [ProtoMember(1, IsRequired = true)]
        [SuppressMessage("Performance", "CA1819:Свойства не должны возвращать массивы", Justification = "<Ожидание>")]
        public byte[] Bytes { get; set; }

        public AccessToken(byte[] bytes)
        {
            Debug.Assert(bytes != null);
            Bytes = bytes;
        }

        public static implicit operator AccessToken(byte[] rawToken)
        {
            return new AccessToken(rawToken);
        }

        [SuppressMessage("Usage", "CA2225:Для перегрузок операторов существуют варианты с именами", Justification = "Предлагает дичь")]
        public static implicit operator byte[](AccessToken self)
        {
            return self.Bytes;
        }

        //public static AccessToken From(byte[] rawToken)
        //{
        //    return new AccessToken(rawToken);
        //}

        //public byte[] AsRawBytes()
        //{
        //    return Bytes;
        //}

        public override bool Equals(object? obj)
        {
            if (obj is AccessToken other)
                return Equals(other: other);

            return false;
        }

        public override int GetHashCode()
        {
            return Bytes.GetHashCode();
        }

        public static bool operator ==(AccessToken left, AccessToken right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AccessToken left, AccessToken right)
        {
            return !(left == right);
        }

        public bool Equals(AccessToken other)
        {
            if (Bytes == other.Bytes)
                return true;

            if (Bytes.Length == other.Bytes.Length)
            {
                return Enumerable.SequenceEqual(Bytes, other.Bytes);
            }
            return false;
        }

        //internal sealed class AccessTokenJsonConverter : JsonConverter<AccessToken>
        //{
        //    public override AccessToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        //    {
        //        SerializerDecorator decorator = JsonSerializer.Deserialize<SerializerDecorator>(ref reader, options);
        //        return new AccessToken(decorator.Bytes);
        //    }

        //    public override void Write(Utf8JsonWriter writer, AccessToken value, JsonSerializerOptions options)
        //    {
        //        var decorator = new SerializerDecorator
        //        {
        //            Bytes = value.Bytes,
        //        };
        //        JsonSerializer.Serialize(writer, decorator, options);
        //    }
        //}

        //[StructLayout(LayoutKind.Auto)]
        //private struct SerializerDecorator
        //{
        //    public byte[] Bytes { get; set; }
        //}
    }
}
