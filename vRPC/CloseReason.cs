using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace vRPC
{
    [DebuggerDisplay(@"\{Gracifully = {Gracifully}\}")]
    public sealed class CloseReason
    {
        /// <summary>
        /// "Соединение не установлено."
        /// </summary>
        internal static readonly CloseReason NoConnectionError = FromException(
            new ConnectionClosedException("Соединение не установлено."));

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

        [DebuggerStepThrough]
        internal static CloseReason FromException(Exception ex, string additionalDescription = null)
        {
            return new CloseReason(ex, null, null, additionalDescription);
        }

        [DebuggerStepThrough]
        internal static CloseReason FromCloseFrame(WebSocketCloseStatus? closeStatus, string closeDescription, string additionalDescription)
        {
            return new CloseReason(null, closeStatus, closeDescription, additionalDescription);
        }

        [DebuggerStepThrough]
        private CloseReason(Exception error, WebSocketCloseStatus? closeStatus, string closeDescription, string additionalDescription)
        {
            Error = error;
            CloseDescription = closeDescription;
            CloseStatus = closeStatus;
            AdditionalDescription = additionalDescription;
        }
    }
}
