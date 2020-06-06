using ProtoBuf;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;

namespace DanilovSoft.vRPC
{
    [ProtoContract]
    internal readonly struct ServerAccessToken
    {
        [ProtoMember(1)]
        public byte[] ClaimsPrincipal { get; }

        [ProtoMember(2, DataFormat = DataFormat.WellKnown)]
        public DateTime Validity { get; }

        public ServerAccessToken(byte[] claimsPrincipal, DateTime validity)
        {
            Debug.Assert(claimsPrincipal != default);

            ClaimsPrincipal = claimsPrincipal;
            Validity = validity;
        }
    }
}
