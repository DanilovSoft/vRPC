﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace vRPC
{
    /// <summary>
    /// Содержит <see cref="MemoryPoolStream"/> в который сериализуется 
    /// сообщение и заголовок для отправки удалённой стороне.
    /// Необходимо обязательно выполнить Dispose.
    /// </summary>
    internal sealed class SerializedMessageToSend : IDisposable
    {
        /// <summary>
        /// Содержит сериализованное сообщение типа <see cref="RequestMessageDto"/> или любой 
        /// другой тип если это ответ на запрос.
        /// Заголовок располагается в конце этого стрима, так как мы не можем сформировать заголовок 
        /// до сериализации тела сообщения.
        /// </summary>
        public MemoryPoolStream MemoryStream { get; } = new MemoryPoolStream();
        public Message MessageToSend { get; }
        /// <summary>
        /// Уникальный идентификатор который будет отправлен удалённой стороне.
        /// </summary>
        public ushort Uid { get; set; }
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
        public SerializedMessageToSend(Message messageToSend)
        {
            MessageToSend = messageToSend;
        }

        public void Dispose()
        {
            MemoryStream.Dispose();
        }
    }
}