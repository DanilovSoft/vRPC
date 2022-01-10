
using ProtoBuf;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;

namespace DanilovSoft.vRPC
{
    [Serializable]
    [ProtoContract]
    [DebuggerDisplay(@"\{{TimeLeft,nq}\}")]
    public struct BearerToken : IEquatable<BearerToken>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string TimeLeft
        {
            get
            {
                var timeLeft = ExpiresAt - DateTime.Now;
                string format = timeLeft.ToString((timeLeft < TimeSpan.Zero ? "\\-" : "") + "dd\\.hh\\:mm\\:ss", CultureInfo.InvariantCulture);
                if (timeLeft > TimeSpan.Zero)
                {
                    return "TimeLeft: " + format;
                }
                else
                {
                    return "Expired: " + format;
                }
            }
        }

        /// <summary>
        /// Зашифрованное тело токена.
        /// </summary>
        [JsonPropertyName("AccessToken")]
        [ProtoMember(1, IsRequired = true)]
        public AccessToken AccessToken { get; set; }

        /// <summary>
        /// Время актуальности токена.
        /// </summary>
        [JsonPropertyName("ExpiresAt")]
        [ProtoMember(2, IsRequired = true, DataFormat = DataFormat.WellKnown)]
        public DateTime ExpiresAt { get; set; }

        public BearerToken(AccessToken accessToken, DateTime expiresAt)
        {
            AccessToken = accessToken;
            ExpiresAt = expiresAt;
        }

        public override bool Equals(object? obj)
        {
            if (obj is BearerToken other)
                return Equals(other: other);

            return false;
        }

        public bool Equals(BearerToken other)
        {
            return ExpiresAt == other.ExpiresAt && AccessToken == other.AccessToken;
        }

        public override int GetHashCode()
        {
            return (ExpiresAt, AccessToken).GetHashCode();
        }

        public static bool operator ==(BearerToken left, BearerToken right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BearerToken left, BearerToken right)
        {
            return !(left == right);
        }
    }
}
