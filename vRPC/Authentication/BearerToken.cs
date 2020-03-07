
using ProtoBuf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DanilovSoft.vRPC
{
    //[JsonObject]
    [ProtoContract]
    [DebuggerDisplay(@"\{TimeLeft = {TimeLeft,nq}\}")]
    public readonly struct BearerToken
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string TimeLeft => (ExpiresAt - DateTime.Now).ToString(@"dd\.hh\:mm\:ss", CultureInfo.InvariantCulture);

        /// <summary>
        /// Зашифрованное тело токена.
        /// </summary>
        [JsonPropertyName("AccessToken")]
        [ProtoMember(1, IsRequired = true)]
        public AccessToken AccessToken { get; }

        /// <summary>
        /// Время актуальности токена.
        /// </summary>
        [JsonPropertyName("ExpiresAt")]
        [ProtoMember(2, IsRequired = true, DataFormat = DataFormat.WellKnown)]
        public DateTime ExpiresAt { get; }

        internal BearerToken(byte[] accessToken, DateTime expiresAt)
        {
            AccessToken = accessToken;
            ExpiresAt = expiresAt;
        }
    }
}
