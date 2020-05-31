using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Content
{
    public sealed class ReadOnlyMemoryContent : VRpcContent
    {
        private readonly ReadOnlyMemory<byte> _content;

        public ReadOnlyMemoryContent(ReadOnlyMemory<byte> content)
        {
            _content = content;
        }
    }
}
