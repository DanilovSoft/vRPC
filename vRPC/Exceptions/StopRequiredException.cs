using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    [Serializable]
    public class StopRequiredException : Exception
    {
        public StopRequiredException() : base("Сервис находится в режиме остановки.")
        {

        }

        public StopRequiredException(TimeSpan afterTimeout) : base($"Сервис был остановлен по запросу пользователя с" +
            $" дополнительным таймаутом для завершения выполняющихся запросов ({afterTimeout}).")
        {

        }
    }
}
