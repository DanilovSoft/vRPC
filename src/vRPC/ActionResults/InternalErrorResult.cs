namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Код ошибки -32603, аналогично 500.
    /// </summary>
    internal sealed class InternalErrorResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InternalError;
        private readonly string _message;

        /// <summary>
        /// Код ошибки -32603, аналогично 500.
        /// </summary>
        public InternalErrorResult(string message = "Invalid Request")
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
