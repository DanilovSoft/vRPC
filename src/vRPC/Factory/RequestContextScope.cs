using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class RequestContextScope
    {
        public IGetProxy? ConnectionContext { get; set; }
    }
}
