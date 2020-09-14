using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC.Context
{
    internal sealed class VErrorResponse : IMessageToSend
    {
        internal int Id { get; }
        private IActionResult _errorResult { get; }

        public VErrorResponse(int id, IActionResult errorResult)
        {
            Id = id;
            _errorResult = errorResult;
        }

        internal ArrayBufferWriter<byte> Serialize(out int headerSize)
        {
            var buffer = new ArrayBufferWriter<byte>();
            bool dispose = true;
            var context = new ActionContext(Id, null, buffer);
            try
            {
                // Сериализуем ответ.
                _errorResult.WriteVRpcResult(ref context);

                headerSize = AppendHeader(buffer, Id, context.StatusCode, context.ProducesEncoding);

                dispose = false; // Предотвратить Dispose.
                return buffer;
            }
            finally
            {
                if (dispose)
                    buffer.Dispose();
            }
        }

        /// <summary>
        /// Сериализует хэдер в стрим сообщения.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private static int AppendHeader(ArrayBufferWriter<byte> buffer, int id, StatusCode responseCode, string? encoding)
        {
            var header = new HeaderDto(id, buffer.WrittenCount, encoding, responseCode);

            // Записать заголовок в конец стрима. Не бросает исключения.
            int headerSize = header.SerializeJson(buffer);

            // Запомним размер хэдера.
            return headerSize;
        }
    }
}
