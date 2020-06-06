using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Ответ на запрос для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Result: {ActionResult}\}")]
    internal sealed class ResponseMessage : IMessageMeta
    {
        /// <summary>
        /// Идентификатор скопированный из запроса.
        /// </summary>
        public int Uid { get; }
        /// <summary>
        /// Результат вызова метода контроллера.
        /// Может быть <see cref="IActionResult"/> или произвольный объект пользователя.
        /// Может быть Null если результат контроллера - void.
        /// </summary>
        public object? ActionResult { get; }
        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        public RequestToInvoke? ReceivedRequest { get; private set; }
        public bool IsRequest => false;
        public bool IsNotificationRequest => false;

        /// <summary>
        /// Конструктор ответа в случае ошибки десериализации запроса.
        /// </summary>
        public ResponseMessage(int uid, object rawResult)
        {
            Uid = uid;
            ActionResult = rawResult;
            ReceivedRequest = null;
        }

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        /// <param name="receivedRequest"></param>
        /// <param name="actionResult"></param>
        [DebuggerStepThrough]
        public ResponseMessage(RequestToInvoke receivedRequest, object? actionResult)
        {
            Debug.Assert(receivedRequest.Uid != null);

            ReceivedRequest = receivedRequest;
            Uid = receivedRequest.Uid!.Value;
            ActionResult = actionResult;
        }
    }
}
