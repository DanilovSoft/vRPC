using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;

namespace vRPC
{
    public readonly struct CloseReason
    {
        internal static readonly CloseReason NotConnectedError = FromException(
            new InvalidOperationException("Произошло обращение к Completion до того как соединение было установлено."));

        /// <summary>
        /// Если разъединение завершилось грациозно — <see langword="true"/>.
        /// </summary>
        public bool Gracifully => Error == null;
        /// <summary>
        /// Может быть <see langword="null"/> если разъединение завершилось грациозно.
        /// </summary>
        public readonly Exception Error { get; }
        /// <summary>
        /// Сообщение от удалённой стороны указывающее причину разъединения.
        /// Если текст совпадает с переданным в метод Stop то разъединение произошло по вашей инициативе.
        /// Может быть <see langword="null"/>.
        /// </summary>
        public readonly string CloseDescription { get; }
        public readonly string AdditionalDescription { get; }
        internal readonly WebSocketCloseStatus? CloseStatus { get; }

        internal static CloseReason FromException(Exception ex, string additionalDescription = null)
        {
            return new CloseReason(ex, null, null, additionalDescription);
        }

        internal static CloseReason FromCloseFrame(WebSocketCloseStatus? closeStatus, string closeDescription)
        {
            return new CloseReason(null, closeStatus, closeDescription, null);
        }

        private CloseReason(Exception error, WebSocketCloseStatus? closeStatus, string closeDescription, string additionalDescription)
        {
            Error = error;
            CloseDescription = closeDescription;
            CloseStatus = closeStatus;
            AdditionalDescription = additionalDescription;
        }
    }
}
