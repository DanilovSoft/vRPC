using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public class StopRequiredException : Exception
    {
        public StopRequired StopRequiredState { get; }

        internal StopRequiredException(StopRequired stopRequired) : base(CreateMessage(stopRequired))
        {
            Debug.Assert(stopRequired != null);
            StopRequiredState = stopRequired;
        }

        public StopRequiredException(string message) : base(message)
        {

        }

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
