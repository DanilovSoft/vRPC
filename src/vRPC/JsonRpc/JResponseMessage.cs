using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc
{
    internal sealed class JResponseMessage : IMessageToSend
    {
        public JResponseMessage(int id, object result)
        {

        }
    }
}
