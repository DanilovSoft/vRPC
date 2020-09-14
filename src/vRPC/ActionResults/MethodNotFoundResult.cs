using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{{_message}\}")]
    internal sealed class MethodNotFoundResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.MethodNotFound;
        private readonly string _message;

        public MethodNotFoundResult()
        {
            _message = "Method not found";
        }

        public MethodNotFoundResult(string message)
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
    }
}
