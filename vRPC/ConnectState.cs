using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    public enum ConnectState
    {
        /// <summary>
        /// Не удалось установить соединение.
        /// </summary>
        RetryLater,
        /// <summary>
        /// Соединение успешно установлено.
        /// </summary>
        Connected,
        /// <summary>
        /// Был запрос на остановку сервиса – переподключать данный экземпляр больше нельзя.
        /// </summary>
        Stopped,
    }
}
