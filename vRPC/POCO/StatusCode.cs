namespace vRPC
{
    /// <summary>
    /// Код состояния передаваемого сообщения.
    /// </summary>
    public enum StatusCode : byte
    {
        Ok = 20,
        Request = 21,
        BadRequest = 40,
        Unauthorized = 41,
        ActionNotFound = 44,
        /// <summary>
        /// Unprocessable Entity.
        /// </summary>
        InvalidRequestFormat = 42,
        InternalError = 50,
    }
}
