namespace DanilovSoft.vRPC
{
    public enum ConnectionState
    {
        /// <summary>
        /// Не удалось установить соединение.
        /// </summary>
        SocketError,
        /// <summary>
        /// Во время подключения произошел запрос на остановку сервиса – использовать данный экземпляр больше нельзя.
        /// </summary>
        ShutdownRequest,
        /// <summary>
        /// Соединение успешно установлено.
        /// </summary>
        Connected,
    }
}
