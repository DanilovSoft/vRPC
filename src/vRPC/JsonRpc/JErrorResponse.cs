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
        internal int Id { get; }
        internal IJActionResult Result { get; }

        /// <summary>
        /// Конструктор ответа в случае ошибки десериализации запроса.
        /// </summary>
        /// <param name="actionResult">Может быть <see cref="IActionResult"/> или произвольный объект пользователя.</param>
        [DebuggerStepThrough]
        public JErrorResponse(int id, IJActionResult actionResult)
        {
            Id = id;
            Result = actionResult;
        }

        internal ArrayBufferWriter<byte> Serialize()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                var actionContext = new ActionContext(Id, method: null, buffer);

                // Сериализуем ответ.
                Result.ExecuteResult(ref actionContext);

                toDispose = null; // Предотвратить Dispose.
                return buffer;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }
    }
}
