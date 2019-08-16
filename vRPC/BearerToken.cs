using Newtonsoft.Json;
using ProtoBuf;
using System;

namespace vRPC
{
    [JsonObject]
    [ProtoContract]
    public sealed class BearerToken
    {
        /// <summary>
        /// Зашифрованное тело токена.
        /// </summary>
        [JsonProperty]
        [ProtoMember(1, IsRequired = true)]
        public byte[] Key { get; set; }

        /// <summary>
        /// Время актуальности токена.
        /// </summary>
        [JsonProperty]
        [ProtoMember(2, IsRequired = true)]
        public TimeSpan ExpiresAt { get; set; }
    }
}
