using System.ComponentModel;

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
        /// Invalid JSON was received by the server.
        /// An error occurred on the server while parsing the JSON text.
        /// </summary>
        [Description("Parse error")]
        ParseError = -32700, // Мы не поддерживаем это говно. Выполняется закрытие сокета с сообщением.
        /// <summary>
        /// The JSON sent is not a valid Request object.
        /// </summary>
        [Description("Invalid Request")]
        InvalidRequest = -32600,
        /// <summary>
        /// The method does not exist / is not available.
        /// </summary>
        [Description("Method not found")]
        MethodNotFound = -32601,
        /// <summary>
        /// Invalid method parameter(s).
        /// </summary>
        [Description("Invalid params")]
        InvalidParams = -32602,
        /// <summary>
        /// Internal JSON-RPC error.
        /// </summary>
        [Description("Internal error")]
        InternalError = -32603
    }
}
