using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    [Serializable]
    public class StopRequiredException : Exception
    {
        public StopRequiredException() : base("Был вызван Stop — использовать этот экземпляр больше нельзя.")
        {

        }

        public StopRequiredException(string message) : base(message)
        {

        }

        public StopRequiredException(TimeSpan afterTimeout) : base($"Сервис был остановлен по запросу пользователя с" +
            $" дополнительным таймаутом для завершения выполняющихся запросов ({afterTimeout}).")
        {

        }
    }
}
