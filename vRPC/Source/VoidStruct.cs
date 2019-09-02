using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC.Source
{
    internal readonly struct VoidStruct
    {
        public static readonly VoidStruct Instance = new VoidStruct();
    }
}
