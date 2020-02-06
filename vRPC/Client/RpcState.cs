using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public enum RpcState
    {
        Closed,
        Open,
        /// <summary>
        /// Произошел запрос на остановку сервиса – подключать данный экземпляр больше нельзя.
        /// Причину остановки можно узнать через свойство <see cref="RpcClient.StopRequiredState"/>.
        /// </summary>
        ShutdownRequest,
    }
}
