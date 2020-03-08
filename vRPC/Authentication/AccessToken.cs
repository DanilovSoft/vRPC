using ProtoBuf;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DanilovSoft.vRPC
{
    [Serializable] // Разрешим юзеру хранить токен в любом виде.
    [ProtoContract]
    [JsonConverter(typeof(AccessTokenJsonConverter))]
    public readonly struct AccessToken : IEquatable<AccessToken>, ISerializable
    {
        [ProtoMember(1, IsRequired = true)]
        private readonly byte[] _rawToken;
        public int Length => _rawToken.Length;

        // Для десериализации через ISerializable.
        private AccessToken(SerializationInfo info, StreamingContext _)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            _rawToken = (byte[])info.GetValue("token", typeof(byte[]));
        }

        public AccessToken(byte[] rawToken)
        {
            Debug.Assert(rawToken != null);
            _rawToken = rawToken;
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            info.AddValue("token", _rawToken);
        }

        public static implicit operator AccessToken(byte[] rawToken)
        {
            return new AccessToken(rawToken);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2225:Для перегрузок операторов существуют варианты с именами", Justification = "Предлагает дичь")]
        public static implicit operator byte[](AccessToken self)
        {
            return self._rawToken;
        }

        public static AccessToken From(byte[] rawToken)
        {
            return new AccessToken(rawToken);
        }

        public byte[] AsRawBytes()
        {
            return _rawToken;
        }

        public override bool Equals(object obj)
        {
            if (obj is AccessToken other)
                return Equals(other: other);

            return false;
        }

        public override int GetHashCode()
        {
            return _rawToken.GetHashCode();
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
            if (_rawToken == other._rawToken)
                return true;

            if (_rawToken.Length == other._rawToken.Length)
            {
                return _rawToken.SequenceEqual(other._rawToken);
            }
            return false;
        }

        internal sealed class AccessTokenJsonConverter : JsonConverter<AccessToken>
        {
            public override AccessToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                byte[] rawToken = JsonSerializer.Deserialize<byte[]>(ref reader, options);
                return new AccessToken(rawToken);
            }

            public override void Write(Utf8JsonWriter writer, AccessToken value, JsonSerializerOptions options)
            {
                JsonSerializer.Serialize(writer, value._rawToken, options);
            }
        }
    }
}
