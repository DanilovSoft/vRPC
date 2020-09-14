using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class JErrorResponse : IMessageToSend
    {
        /// <summary>
        /// Идентификатор запроса.
        /// </summary>
        internal int? Id { get; }
        private readonly IActionResult _errorResult;

        /// <summary>
        /// Конструктор ответа в случае ошибки десериализации запроса.
        /// </summary>
        /// <param name="errorResult">Может быть <see cref="IActionResult"/> или произвольный объект пользователя.</param>
        [DebuggerStepThrough]
        public JErrorResponse(int? id, IActionResult errorResult)
        {
            Id = id;
            _errorResult = errorResult;
        }

        internal ArrayBufferWriter<byte> Serialize()
        {
            var buffer = new ArrayBufferWriter<byte>();
            bool dispose = true;
            try
            {
                // Сериализуем ответ.
                _errorResult.WriteJsonRpcResult(Id, buffer);

                dispose = false; // Предотвратить Dispose.
                return buffer;
            }
            finally
            {
                if (dispose)
                    buffer.Dispose();
            }
        }
    }
}
