using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Ответ на запрос для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Result: {Result}\}")]
    internal sealed class ResponseMessage : IMessage
    {
        /// <summary>
        /// Идентификатор скопированный из запроса.
        /// </summary>
        public ushort Uid { get; }
        public object Result { get; }
        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        public RequestContext ReceivedRequest { get; private set; }
        ///// <summary>
        ///// Параметры для удаленного метода.
        ///// </summary>
        //public JToken[] Args { get; }

        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        public ResponseMessage(ushort uid, object rawResult)
        {
            Uid = uid;
            Result = rawResult;
        }

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        /// <param name="receivedRequest"></param>
        /// <param name="rawResult"></param>
        [DebuggerStepThrough]
        public ResponseMessage(RequestContext receivedRequest, object rawResult)
        {
            Debug.Assert(receivedRequest.HeaderDto.Uid != null);

            ReceivedRequest = receivedRequest;
            Uid = receivedRequest.HeaderDto.Uid.Value;
            Result = rawResult;
        }
    }
}
