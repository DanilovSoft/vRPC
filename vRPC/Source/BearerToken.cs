
using ProtoBuf;
using System;

namespace DanilovSoft.vRPC
{
    //[JsonObject]
    [ProtoContract]
    public sealed class BearerToken
    {
        /// <summary>
        /// Зашифрованное тело токена.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("Key")]
        [ProtoMember(1, IsRequired = true)]
        public byte[] Key { get; set; }

        /// <summary>
        /// Время актуальности токена.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("ExpiresAt")]
        [ProtoMember(2, IsRequired = true)]
        public TimeSpan ExpiresAt { get; set; }
    }
}
