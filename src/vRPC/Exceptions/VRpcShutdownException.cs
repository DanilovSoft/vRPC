using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Происходит при обращении к выключенному экземпляру или находящемуся в процессе отключения по запросу пользователя.
    /// </summary>
    [Serializable]
    public sealed class VRpcShutdownException : VRpcException
    {
        public ShutdownRequest? ShutdownRequest { get; }

        public VRpcShutdownException() { }

        public VRpcShutdownException(string message) : base(message) { }

        public VRpcShutdownException(string message, Exception innerException) : base(message, innerException) { }

        internal VRpcShutdownException(ShutdownRequest shutdownRequest) : base(CreateExceptionMessage(shutdownRequest))
        {
            Debug.Assert(shutdownRequest != null);
            ShutdownRequest = shutdownRequest;
        }

        private static string CreateExceptionMessage(ShutdownRequest shutdownRequest)
        {
            if (!string.IsNullOrEmpty(shutdownRequest.CloseDescription))
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Shutdown (DisconnectTimeout: {shutdownRequest.ShutdownTimeout.TotalSeconds:0.#} сек.) со следующим объяснением причины: '{shutdownRequest.CloseDescription}'.";
            }
            else
            {
                return $"Использовать этот экземпляр больше нельзя — был вызван " +
                    $"Shutdown (DisconnectTimeout: {shutdownRequest.ShutdownTimeout.TotalSeconds:0.#} сек.) без объяснения причины.";
            }
        }

#pragma warning disable CA1801
        private VRpcShutdownException(SerializationInfo serializationInfo, StreamingContext streamingContext)
        {
            
        }
#pragma warning restore CA1801
    }
}
