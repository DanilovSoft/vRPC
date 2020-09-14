using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{InvalidRequestResult: {_message}\}")]
    internal sealed class InvalidRequestResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InvalidRequest;
        private readonly string _message;

        public InvalidRequestResult()
        {
            _message = "Invalid Request";
        }

        public InvalidRequestResult(string message)
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
