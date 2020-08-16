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
        /// Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        public ControllerActionMeta? ActionMeta { get; private set; }
        public bool IsRequest => false;
        public bool IsNotificationRequest => false;
        public bool TcpNoDelay => ActionMeta?.TcpNoDelay ?? false;
        public bool IsJsonRpc => throw new NotImplementedException();

        #region ctor

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        [DebuggerStepThrough]
        public ResponseMessage(int uid, ControllerActionMeta actionMeta, object? actionResult)
        {
            ActionMeta = actionMeta;
            Uid = uid;
            ActionResult = actionResult;
        }

        /// <summary>
        /// Конструктор ответа в случае ошибки десериализации запроса.
        /// </summary>
        /// <param name="rawResult">Может быть <see cref="IActionResult"/> или произвольный объект пользователя.</param>
        public ResponseMessage(int uid, object rawResult)
        {
            Uid = uid;
            ActionResult = rawResult;
            ActionMeta = null;
        }
        #endregion
    }
}
