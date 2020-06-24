namespace DanilovSoft.vRPC
{
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
        ActionNotFound = 44,
        /// <summary>
        /// Unprocessable Entity.
        /// </summary>
        InvalidRequestFormat = 42,
        InternalError = 50,
    }
}
