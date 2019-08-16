using ProtoBuf;
using System;

namespace vRPC
{
    [ProtoContract]
    internal struct ServerBearerToken
    {
        [ProtoMember(1)]
        public int UserId;

        [ProtoMember(2, DataFormat = DataFormat.WellKnown)]
        public DateTime Validity;
    }
}
