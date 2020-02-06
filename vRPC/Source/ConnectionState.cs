using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public enum ConnectionState
    {
        /// <summary>
        /// Соединение успешно установлено.
        /// </summary>
        Connected,
        /// <summary>
        /// Не удалось установить соединение.
        /// </summary>
        SocketError,
        /// <summary>
        /// Во время подключения произошел запрос на остановку сервиса – использовать данный экземпляр больше нельзя.
        /// </summary>
        ShutdownRequest,
    }
}
