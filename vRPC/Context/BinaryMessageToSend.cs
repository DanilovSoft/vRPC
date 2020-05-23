using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Является запросом или ответом на запрос.
    /// Содержит <see cref="MemoryStream"/> в который сериализуется 
    /// сообщение и заголовок для отправки удалённой стороне.
    /// Необходимо обязательно выполнить Dispose.
    /// </summary>
    internal sealed class BinaryMessageToSend : IDisposable
    {
#if DEBUG
        // Что-бы видеть контент в режиме отладки.
        private string? DebugJson
        {
            get
            {
                if (MemPoolStream?.Length > 0)
                {
                    byte[] copy = MemPoolStream.ToArray();
                    string j = Encoding.UTF8.GetString(copy, 0, copy.Length - HeaderSize);
                    return System.Text.Json.JsonDocument.Parse(j).RootElement.ToString();
                }
                return null;
            }
        }
#endif

        private MemoryStream? _memPoolStream;
        /// <summary>
        /// Содержит сериализованное сообщение типа <see cref="RequestMessageDto"/> или любой 
        /// другой тип если это ответ на запрос.
        /// Заголовок располагается в конце этого стрима, так как мы не можем сформировать заголовок 
        /// до сериализации тела сообщения.
        /// </summary>
        public MemoryStream MemPoolStream
        {
            get
            {
                Debug.Assert(_memPoolStream != null);
                return _memPoolStream;
            }
        }
        /// <summary>
        /// Запрос или ответ на запрос.
        /// </summary>
        public IMessageMeta MessageToSend { get; }
        /// <summary>
        /// Уникальный идентификатор который будет отправлен удалённой стороне.
        /// Может быть Null когда не требуется ответ на запрос.
        /// </summary>
        public int? Uid { get; set; }
        public StatusCode? StatusCode { get; set; }
        public string? ContentEncoding { get; set; }
        /// <summary>
        /// Размер хэдера располагающийся в конце стрима.
        /// </summary>
        public int HeaderSize { get; set; }

        /// <summary>
        /// Содержит <see cref="MemoryStream"/> в который сериализуется сообщение и заголовок.
        /// Необходимо обязательно выполнить Dispose.
        /// </summary>
        public BinaryMessageToSend(IMessageMeta messageToSend)
        {
            MessageToSend = messageToSend;

            // Арендуем заранее под максимальный размер хэдера.
            _memPoolStream = GlobalVars.RecyclableMemory.GetStream("SerializedMessageToSend", 32);
        }

        /// <summary>
        /// Возвращает арендованную память обратно в пул.
        /// </summary>
        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _memPoolStream, null);
            Debug.Assert(stream != null, "Другой поток уже выполнил Dispose — это не страшно но такого не должно случаться");
            stream?.Dispose();
        }
    }
}
