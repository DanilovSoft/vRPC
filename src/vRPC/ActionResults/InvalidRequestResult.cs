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

        public InvalidRequestResult(string message = "Invalid Request")
        {
            _message = message;
        }

        public void WriteVRpcResult(ref ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseBuffer.WriteStringBinary(_message);
        }

        void IActionResult.WriteJsonRpcResult(int? id, ArrayBufferWriter<byte> buffer)
        {
            JsonRpcSerializer.SerializeErrorResponse(buffer, DefaultStatusCode, _message, id);
        }

        ArrayBufferWriter<byte> IActionResult.WriteJsonRpcResult(int? id)
        {
            JsonRpcSerializer.TrySerializeErrorResponse(id, DefaultStatusCode, _message, out var buffer, out _);
            return buffer;
        }
    }
}
