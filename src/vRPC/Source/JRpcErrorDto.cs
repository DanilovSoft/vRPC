using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC
{
    [StructLayout(LayoutKind.Auto)]
    internal struct JRpcErrorDto
    {
        public StatusCode Code;
        public string? Message;

        public static bool operator ==(in JRpcErrorDto left, in JRpcErrorDto right)
        {
            return left.Code == right.Code;
        }

        public static bool operator !=(in JRpcErrorDto left, in JRpcErrorDto right)
        {
            return left.Code != right.Code;
        }
    }
}
