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
        public ShutdownRequest StopRequiredState { get; }

        public StopRequiredException() { }

        public StopRequiredException(string message) : base(message) { }

        public StopRequiredException(string message, Exception innerException) : base(message, innerException) { }

        internal StopRequiredException(ShutdownRequest stopRequired) : base(CreateExceptionMessage(stopRequired))
        {
            Debug.Assert(stopRequired != null);
            StopRequiredState = stopRequired;
        }

        private static string CreateExceptionMessage(ShutdownRequest stopRequired)
        {
            if (!string.IsNullOrEmpty(stopRequired.CloseDescription))
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Shutdown (DisconnectTimeout: {stopRequired.ShutdownTimeout}) со следующим объяснением причины: '{stopRequired.CloseDescription}'.";
            }
            else
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Shutdown (DisconnectTimeout: {stopRequired.ShutdownTimeout}) без дополнительного объяснения причины.";
            }
        }

#pragma warning disable CA1801
        private StopRequiredException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
