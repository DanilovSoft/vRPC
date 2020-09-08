using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public class StatusCodeResult : ActionResult
    {
        internal StatusCodeResult(StatusCode statusCode) : base(statusCode)
        {
        }

        private protected override void FinalWriteJsonRpcResult(int id, IBufferWriter<byte> buffer)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }

        private protected override void FinalWriteResult(ref ActionContext context)
        {
            
        }
    }
}
