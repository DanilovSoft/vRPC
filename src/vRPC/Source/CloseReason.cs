using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    //[DebuggerDisplay(@"\{{ToString(),nq}\}")] // Пусть отображается ToString()
    public sealed class CloseReason
    {
        /// <summary>
        /// "Соединение не установлено."
        /// </summary>
        internal static readonly CloseReason NoConnectionGracifully = new CloseReason(null, null, null, "Соединение не установлено", null);
        internal static Task<CloseReason> NoConnectionCompletion = Task.FromResult(NoConnectionGracifully);

        /// <summary>
        /// Является <see langword="true"/> если разъединение завершилось грациозно.
        /// </summary>
        public bool Gracifully => ConnectionError == null;
        /// <summary>
        /// Является <see langword="null"/> если разъединение завершилось грациозно
        /// и не является <see langword="null"/> когда разъединение завершилось не грациозно.
        /// </summary>
        /// <remarks>Может быть производными типа <see cref="VRpcException"/> или <see cref="ObjectDisposedException"/></remarks>
        public Exception? ConnectionError { get; }
        /// <summary>
        /// Сообщение от удалённой стороны указывающее причину разъединения (может быть <see langword="null"/>).
        /// Если текст совпадает с переданным в метод Shutdown то разъединение произошло по вашей инициативе.
        /// </summary>
        public string? CloseDescription { get; }
        /// <summary>
        /// Может быть <see langword="null"/>. Не зависит от <see cref="Gracifully"/>.
        /// </summary>
        public string? AdditionalDescription { get; }
        internal WebSocketCloseStatus? CloseStatus { get; }
        /// <summary>
        /// Если был выполнен запрос на остановку сервиса то это свойство будет не <see langword="null"/>.
        /// </summary>
        public ShutdownRequest? ShutdownRequest { get; }

        [DebuggerStepThrough]
        internal static CloseReason FromException(VRpcShutdownException stopRequiredException)
        {
            return new CloseReason(stopRequiredException, null, null, null, stopRequiredException.ShutdownRequest);
        }

        internal static CloseReason FromException(VRpcException exception, ShutdownRequest? shutdownRequest, string? additionalDescription = null)
        {
            return InnerFromException(exception, shutdownRequest, additionalDescription);
        }

        internal static CloseReason FromException(ObjectDisposedException exception, ShutdownRequest? shutdownRequest, string? additionalDescription = null)
        {
            return InnerFromException(exception, shutdownRequest, additionalDescription);
        }

        private static CloseReason InnerFromException(Exception ex, ShutdownRequest? shutdownRequest, string? additionalDescription = null)
        {
            Debug.Assert(ex is VRpcException || ex is ObjectDisposedException);

            return new CloseReason(ex, null, null, additionalDescription, shutdownRequest);
        }

        [DebuggerStepThrough]
        internal static CloseReason FromCloseFrame(WebSocketCloseStatus? closeStatus, string? closeDescription, string? additionalDescription, ShutdownRequest? shutdownRequest)
        {
            return new CloseReason(null, closeStatus, closeDescription, additionalDescription, shutdownRequest);
        }

        [DebuggerStepThrough]
        private CloseReason(Exception? exception, WebSocketCloseStatus? closeStatus, string? closeDescription, string? additionalDescription, ShutdownRequest? shutdownRequest)
        {
            ConnectionError = exception;
            CloseDescription = closeDescription;
            CloseStatus = closeStatus;
            AdditionalDescription = additionalDescription;
            ShutdownRequest = shutdownRequest;
        }

        public sealed override string ToString()
        {
            if (Gracifully)
            {
                if(string.IsNullOrEmpty(CloseDescription))
                {
                    if (string.IsNullOrEmpty(AdditionalDescription))
                    {
                        return "Удалённая сторона выполнила нормальное закрытие без объяснения причины";
                    }
                    else
                    {
                        return AdditionalDescription;
                    }
                }
                else
                {
                    return $"Удалённая сторона выполнила нормальное закрытие: '{CloseDescription}'";
                }
            }
            else
            {
                return $"Соединение оборвано: {AdditionalDescription ?? ConnectionError!.Message}";
            }
        }
    }
}
