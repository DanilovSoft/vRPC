using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Context;
using DanilovSoft.vRPC.Source;

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
