using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит <see cref="MemoryPoolStream"/> в который сериализуется 
    /// сообщение и заголовок для отправки удалённой стороне.
    /// Необходимо обязательно выполнить Dispose.
    /// </summary>
    internal sealed class SerializedMessageToSend : IDisposable
    {
#if DEBUG
        private string DebugJson
        {
            get
            {
                var copy = MemPoolStream.ToArray();
                string j = Encoding.UTF8.GetString(copy);
                return JToken.Parse(j).ToString(Newtonsoft.Json.Formatting.Indented);
            }
        }
#endif

        /// <summary>
        /// Содержит сериализованное сообщение типа <see cref="RequestMessageDto"/> или любой 
        /// другой тип если это ответ на запрос.
        /// Заголовок располагается в конце этого стрима, так как мы не можем сформировать заголовок 
        /// до сериализации тела сообщения.
        /// </summary>
        public MemoryPoolStream MemPoolStream { get; } = new MemoryPoolStream();
        public IMessage MessageToSend { get; }
        /// <summary>
        /// Уникальный идентификатор который будет отправлен удалённой стороне.
        /// </summary>
        public ushort? Uid { get; set; }
        public StatusCode? StatusCode { get; set; }
        public string ContentEncoding { get; set; }
        /// <summary>
        /// Размер хэдера располагающийся в конце стрима.
        /// </summary>
        public int HeaderSize { get; set; }

        /// <summary>
        /// Содержит <see cref="MemoryPoolStream"/> в который сериализуется сообщение и заголовок.
        /// Необходимо обязательно выполнить Dispose.
        /// </summary>
        public SerializedMessageToSend(IMessage messageToSend)
        {
            MessageToSend = messageToSend;
        }

        /// <summary>
        /// Возвращает арендрванную память обратно в пул.
        /// </summary>
        public void Dispose()
        {
            MemPoolStream.Dispose();
        }
    }
}
