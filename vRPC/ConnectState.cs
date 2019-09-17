using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public enum ConnectState
    {
        /// <summary>
        /// Не удалось установить соединение.
        /// </summary>
        NotConnected,
        /// <summary>
        /// Соединение успешно установлено.
        /// </summary>
        Connected,
        /// <summary>
        /// Во время подключения произошел запрос на остановку сервиса – подключать данный экземпляр больше нельзя.
        /// </summary>
        StopRequired,
    }
}
