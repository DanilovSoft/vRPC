﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    /// <summary>
    /// Сообщение для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Result: {Result}\}")]
    internal sealed class ResponseMessage : Message
    {
        /// <summary>
        /// Идентификатор скопированный из запроса.
        /// </summary>
        public ushort Uid { get; }
        public object Result { get; }
        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        public RequestMessageDto ReceivedRequest { get; private set; }

        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        public ResponseMessage(ushort uid, object result) : base()
        {
            Uid = uid;
            Result = result;
        }

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        /// <param name="receivedRequest"></param>
        /// <param name="rawResult"></param>
        public ResponseMessage(RequestMessageDto receivedRequest, object rawResult) 
            : this(receivedRequest.Header.Uid, rawResult)
        {
            ReceivedRequest = receivedRequest;
        }
    }
}
