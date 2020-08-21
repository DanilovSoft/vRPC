using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Ответ на запрос для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Result: {MethodResult}\}")]
    internal sealed class ResponseMessage : IMessageMeta
    {
        /// <summary>
        /// Идентификатор скопированный из запроса.
        /// </summary>
        internal int Id { get; }
        /// <summary>
        /// Результат вызова метода контроллера.
        /// Может быть <see cref="IActionResult"/> или произвольный объект пользователя.
        /// Может быть Null если результат контроллера - void.
        /// </summary>
        internal object? MethodResult { get; }
        /// <summary>
        /// Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        internal ControllerMethodMeta? Method { get; }
        public bool IsRequest => false;
        public bool IsNotificationRequest => false;
        public bool TcpNoDelay => Method?.TcpNoDelay ?? false;
        public bool IsJsonRpc => throw new NotImplementedException();

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        [DebuggerStepThrough]
        public ResponseMessage(int id, ControllerMethodMeta method, object? actionResult)
        {
            Id = id;
            Method = method;
            MethodResult = actionResult;
        }

        /// <summary>
        /// Конструктор ответа в случае ошибки десериализации запроса.
        /// </summary>
        /// <param name="actionResult">Может быть <see cref="IActionResult"/> или произвольный объект пользователя.</param>
        [DebuggerStepThrough]
        public ResponseMessage(int id, IActionResult actionResult)
        {
            Id = id;
            MethodResult = actionResult;
            Method = null;
        }
    }
}
