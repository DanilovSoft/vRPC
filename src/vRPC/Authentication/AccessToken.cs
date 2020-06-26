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
    }
}
