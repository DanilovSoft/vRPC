using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Gracifully = {Gracifully}\}")]
    public sealed class CloseReason
    {
        /// <summary>
        /// "Соединение не установлено."
        /// </summary>
        internal static readonly CloseReason NoConnectionError = FromException(
            new ConnectionClosedException("Соединение не установлено."), null);

        /// <summary>
        /// "Соединение не установлено."
        /// </summary>
        internal static readonly CloseReason NoConnectionGracifully = new CloseReason(null,null, null, "Соединение не установлено.", null);

        /// <summary>
        /// Если разъединение завершилось грациозно — <see langword="true"/>.
        /// </summary>
        public bool Gracifully => Error == null;
        /// <summary>
        /// Может быть <see langword="null"/> если разъединение завершилось грациозно.
        /// </summary>
        public Exception Error { get; }
        /// <summary>
        /// Сообщение от удалённой стороны указывающее причину разъединения.
        /// Если текст совпадает с переданным в метод Stop то разъединение произошло по вашей инициативе.
        /// Может быть <see langword="null"/>.
        /// </summary>
        public string CloseDescription { get; }
        /// <summary>
        /// Может быть <see langword="null"/>. Не зависит от <see cref="Gracifully"/>.
        /// </summary>
        public string AdditionalDescription { get; }
        internal WebSocketCloseStatus? CloseStatus { get; }
        /// <summary>
        /// Если был выполнен запрос на остановку сервиса то это свойство будет не <see langword="null"/>.
        /// </summary>
        public StopRequired StopRequest { get; }

        [DebuggerStepThrough]
        internal static CloseReason FromException(StopRequiredException stopRequiredException)
        {
            return new CloseReason(stopRequiredException, null, null, null, stopRequiredException.StopRequired);
        }

        [DebuggerStepThrough]
        internal static CloseReason FromException(Exception ex, StopRequired stopRequired, string additionalDescription = null)
        {
            return new CloseReason(ex, null, null, additionalDescription, stopRequired);
        }

        [DebuggerStepThrough]
        internal static CloseReason FromCloseFrame(WebSocketCloseStatus? closeStatus, string closeDescription, string additionalDescription, StopRequired stopRequired)
        {
            return new CloseReason(null, closeStatus, closeDescription, additionalDescription, stopRequired);
        }

        [DebuggerStepThrough]
        private CloseReason(Exception error, WebSocketCloseStatus? closeStatus, string closeDescription, string additionalDescription, StopRequired stopRequired)
        {
            Error = error;
            CloseDescription = closeDescription;
            CloseStatus = closeStatus;
            AdditionalDescription = additionalDescription;
            StopRequest = stopRequired;
        }
    }
}
