using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc
{
    internal sealed class JResponse : IMessageToSend
    {
        internal readonly int Id;
        internal readonly object Result;

        public JResponse(int id, object result)
        {
            Id = id;
            Result = result;
        }
    }
}
