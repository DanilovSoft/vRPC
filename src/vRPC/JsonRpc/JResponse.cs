using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc
{
    internal sealed class JResponse : IMessageToSend
    {
        /// <summary>
        /// Идентификатор запроса.
        /// </summary>
        internal readonly int? Id;
        internal readonly object? MethodResult;
        /// <summary>
        /// Может быть <see langword="null"/> например если ответ это ошибка разбора запроса.
        /// </summary>
        internal ControllerMethodMeta? Method { get; }

        public ManagedConnection Context { get; }

        /// <summary>
        /// Ответ на основе запроса.
        /// </summary>
        [DebuggerStepThrough]
        public JResponse(ManagedConnection context, int id, ControllerMethodMeta method, object? methodResult)
        {
            Context = context;
            Id = id;
            Method = method;
            MethodResult = methodResult;
        }

        /// <summary>
        /// Конструктор ответа в случае ошибки десериализации запроса.
        /// </summary>
        /// <param name="actionResult">Может быть <see cref="IActionResult"/> или произвольный объект пользователя.</param>
        [DebuggerStepThrough]
        public JResponse(ManagedConnection context, int? id, IActionResult actionResult)
        {
            Context = context;
            Id = id;
            MethodResult = actionResult;
            Method = null;
        }
    }
}
