namespace DanilovSoft.vRPC
{
    // https://www.jsonrpc.org/specification#error_object
    /// <summary>
    /// Код состояния передаваемого сообщения.
    /// </summary>
    public enum StatusCode
    {
        None = 0,
        Ok = 20,
        Request = 21,
        BadRequest = 40,
        Unauthorized = 41,
        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        MethodNotFound = -32601,
        /// <summary>
        /// Unprocessable Entity.
        /// </summary>
        InvalidRequestFormat = 42,
        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        InternalError = -32603,
        /// <summary>
        /// Invalid method parameter(s).
        /// </summary>
        InvalidParams = -32602,
    }
}
