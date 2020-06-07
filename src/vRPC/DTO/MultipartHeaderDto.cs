using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ProtoBuf;

namespace DanilovSoft.vRPC.DTO
{
    [ProtoContract]
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay(@"\{Size = {Size}, Encoding = {Encoding}\}")]
    internal readonly struct MultipartHeaderDto
    {
        [ProtoMember(1, IsRequired = true)]
        public int Size { get; }

        [ProtoMember(2, IsRequired = true)]
        public string Encoding { get; }

        public MultipartHeaderDto(int size, string encoding)
        {
            Size = size;
            Encoding = encoding;
            //SelfSize = 0;
        }
    }
}
