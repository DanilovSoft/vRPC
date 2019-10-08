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
        /// <summary>
        /// Результат вызова метода контроллера.
        /// </summary>
        public object ActionResult { get; }
        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        public RequestToInvoke ReceivedRequest { get; private set; }
        public bool IsRequest => false;
        public bool IsNotificationRequest => false;

        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        public ResponseMessage(ushort uid, object rawResult)
        {
            Uid = uid;
            ActionResult = rawResult;
        }

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        /// <param name="receivedRequest"></param>
        /// <param name="rawResult"></param>
        [DebuggerStepThrough]
        public ResponseMessage(RequestToInvoke receivedRequest, object rawResult)
        {
            Debug.Assert(receivedRequest.Uid != null);

            ReceivedRequest = receivedRequest;
            Uid = receivedRequest.Uid.Value;
            ActionResult = rawResult;
        }
    }
}
