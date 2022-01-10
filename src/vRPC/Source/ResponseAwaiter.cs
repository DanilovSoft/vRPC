using System;
using System.Text.Json;
using DanilovSoft.vRPC.Context;

namespace DanilovSoft.vRPC
{
    internal interface IResponseAwaiter : IMessageToSend
    {
        int Id { get; set; }

        /// <summary>
        /// Передает ожидающему потоку исключение как результат запроса.
        /// </summary>
        void TrySetErrorResponse(Exception exception);

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        void TrySetVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload);

        /// <summary>
        /// Передаёт ответ ожидающему потоку.
        /// </summary>
        /// <param name="reader">Json ридер содержащий контент ответа.</param>
        void TrySetJResponse(ref Utf8JsonReader reader);
    }
}
