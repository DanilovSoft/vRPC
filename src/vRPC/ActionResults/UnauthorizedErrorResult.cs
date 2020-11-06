using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class UnauthorizedErrorResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.Unauthorized;
        private readonly string _message;

        public UnauthorizedErrorResult(string message)
        {
            _message = message;
        }

        void IActionResult.WriteJsonRpcResult(int? id, ArrayBufferWriter<byte> buffer)
        {
            JsonRpcSerializer.SerializeErrorResponse(buffer, DefaultStatusCode, _message, id);
        }

        public void WriteVRpcResult(ref ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseBuffer.WriteStringBinary(_message);
        }

        ArrayBufferWriter<byte> IActionResult.WriteJsonRpcResult(int? id)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
