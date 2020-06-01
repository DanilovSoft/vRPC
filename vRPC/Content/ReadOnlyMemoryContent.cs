using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;

namespace DanilovSoft.vRPC
{
    public class ReadOnlyMemoryContent : VRpcContent
    {
        public ReadOnlyMemory<byte> Memory { get; }

        public ReadOnlyMemoryContent(ReadOnlyMemory<byte> content)
        {
            Memory = content;
        }

        protected internal override bool TryComputeLength(out long length)
        {
            length = Memory.Length;
            return true;
        }

        protected internal override Task SerializeToStreamAsync(Stream stream)
        {
            throw new NotImplementedException();
        }
    }
}
