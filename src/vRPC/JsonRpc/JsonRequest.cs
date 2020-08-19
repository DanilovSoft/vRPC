using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc
{
    [StructLayout(LayoutKind.Auto)]
    internal struct JsonRequest
    {
        public int? Id;
        public ControllerMethodMeta Method;
        public object[] Args;
    }
}
