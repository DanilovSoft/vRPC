using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public abstract class ActionResult : IActionResult
    {
        internal StatusCode StatusCode { get; set; }

        internal ActionResult(StatusCode statusCode)
        {
            StatusCode = statusCode;
        }

        public virtual Task ExecuteResultAsync(ref ActionContext context)
        {
            WriteVRpcResult(ref context);
            return Task.CompletedTask;
        }

        public virtual void WriteVRpcResult(ref ActionContext context)
        {
            FinalWriteResult(ref context);
        }

        private protected abstract void FinalWriteResult(ref ActionContext context);

        void IActionResult.WriteJsonRpcResult(int? id, ArrayBufferWriter<byte> buffer)
        {
            InnerWriteJsonRpcResult(id, buffer);
        }

        internal void InnerWriteJsonRpcResult(int? id, ArrayBufferWriter<byte> buffer)
        {
            FinalWriteJsonRpcResult(id, buffer);
        }

        private protected abstract void FinalWriteJsonRpcResult(int? id, IBufferWriter<byte> buffer);

        ArrayBufferWriter<byte> IActionResult.WriteJsonRpcResult(int? id)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
