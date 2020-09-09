using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC.Context
{
    internal sealed class VErrorResponse : IMessageToSend
    {
        internal int Id { get; }
        internal IActionResult ErrorResult { get; }

        public VErrorResponse(int id, IActionResult errorResult)
        {
            Id = id;
            ErrorResult = errorResult;
        }

        internal ArrayBufferWriter<byte> Serialize(out int headerSize)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
