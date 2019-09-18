using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public interface IGetConnection
    {
        ManagedConnection Get();
    }

    internal sealed class GetProxyScope
    {
        public IGetProxy GetProxy { get; set; }
    }
}
