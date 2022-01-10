using System;

namespace DanilovSoft.vRPC
{
    public sealed class InvalidParamsResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InvalidParams;
        private readonly string _message;

        public InvalidParamsResult()
        {
            _message = "Invalid params";
        }

        public InvalidParamsResult(string message)
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
            throw new NotImplementedException();
        }
    }
}
