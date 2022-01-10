using ProtoBuf;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DanilovSoft.vRPC
{
    [ProtoContract]
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct ServerAccessToken
    {
        [ProtoMember(1)]
        public byte[] ClaimsPrincipal { get; }

        [ProtoMember(2, DataFormat = DataFormat.WellKnown)]
        public DateTime Validity { get; }

        public ServerAccessToken(byte[] claimsPrincipal, DateTime validity)
        {
            Debug.Assert(claimsPrincipal != null);

            ClaimsPrincipal = claimsPrincipal;
            Validity = validity;
        }
    }
}
