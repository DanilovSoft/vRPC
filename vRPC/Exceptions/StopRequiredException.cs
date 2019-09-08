using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    [Serializable]
    public class StopRequiredException : Exception
    {
        private const string DefaultMessage = "Был вызван Stop — использовать этот экземпляр больше нельзя.";

        /// <summary>
        /// Причина остановки сервиса указанная пользователем. Может быть <see langword="null"/>.
        /// </summary>
        public string CloseDescription { get; }
        /// <summary>
        /// Максимальное время ожидания остановки сервиса указанное пользователем 
        /// после которого соединение закрывается принудительно.
        /// </summary>
        public TimeSpan Timeout { get; }

        internal StopRequiredException(StopRequired stopRequired) : base(CreateMessage(stopRequired))
        {
            Debug.Assert(stopRequired != null);

            CloseDescription = stopRequired.CloseDescription;
            Timeout = stopRequired.Timeout;
        }

        public StopRequiredException(string message) : base(message)
        {

        }

        //public StopRequiredException(TimeSpan afterTimeout) : base($"Сервис был остановлен по запросу пользователя с" +
        //    $" дополнительным таймаутом для завершения выполняющихся запросов ({afterTimeout}).")
        //{

        //}

        private static string CreateMessage(StopRequired stopRequired)
        {
            if (!string.IsNullOrEmpty(stopRequired.CloseDescription))
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Stop (таймаут: {stopRequired.Timeout}) со следующим объяснением причины: '{stopRequired.CloseDescription}'.";
            }
            else
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Stop (таймаут: {stopRequired.Timeout}) без дополнительного объяснения причины.";
            }
        }
    }
}
