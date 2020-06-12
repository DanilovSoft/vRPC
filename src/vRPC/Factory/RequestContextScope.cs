using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
#pragma warning disable CA1812
    internal sealed class RequestContextScope
    {
        public IGetProxy? ConnectionContext { get; set; }
    }
#pragma warning restore CA1812
}
