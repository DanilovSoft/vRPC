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
        private readonly string _methodName;

        public MethodNotFoundResult(string methodName, string message)
        {
            _methodName = methodName;
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
            // Не может вернуть False.
            JsonRpcSerializer.TrySerializeErrorResponse(id, DefaultStatusCode, _message, out var buffer, out _);

            Debug.Assert(buffer != null);
            return buffer;
        }
    }
}
