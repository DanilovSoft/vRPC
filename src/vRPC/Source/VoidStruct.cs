﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC
{
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct VoidStruct
    {
        internal static readonly object RefInstance = new VoidStruct();
    }
}
