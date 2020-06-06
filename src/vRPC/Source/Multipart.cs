using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC.Source
{
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay(@"\{HeaderLength = {HeaderLength}, ContentLength = {ContentLength}\}")]
    internal readonly struct Multipart
    {
        public int ContentLength { get; }
        public byte HeaderLength { get; }

        [DebuggerStepThrough]
        public Multipart(int contentLength, byte headerLength)
        {
            ContentLength = contentLength;
            HeaderLength = headerLength;
        }
    }
}
