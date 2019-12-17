using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;

namespace DanilovSoft.vRPC
{
    [Serializable]
    public sealed class StopRequiredException : Exception
    {
        public StopRequired StopRequiredState { get; }

        public StopRequiredException()
        {

        }

        public StopRequiredException(string message) : base(message)
        {

        }

        internal StopRequiredException(StopRequired stopRequired) : base(CreateMessage(stopRequired))
        {
            Debug.Assert(stopRequired != null);
            StopRequiredState = stopRequired;
        }

        private static string CreateMessage(StopRequired stopRequired)
        {
            if (!string.IsNullOrEmpty(stopRequired.CloseDescription))
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Stop (DisconnectTimeout: {stopRequired.DisconnectTimeout}) со следующим объяснением причины: '{stopRequired.CloseDescription}'.";
            }
            else
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Stop (DisconnectTimeout: {stopRequired.DisconnectTimeout}) без дополнительного объяснения причины.";
            }
        }

        public StopRequiredException(string message, Exception innerException) : base(message, innerException)
        {
        }

#pragma warning disable CA1801
        private StopRequiredException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
