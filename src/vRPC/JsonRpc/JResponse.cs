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

        internal ArrayBufferWriter<byte> Serialize()
        {
            var buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                if (MethodResult is IActionResult actionResult)
                // Метод контроллера вернул специальный тип.
                {
                    var actionContext = new ActionContext(Id, Method, buffer);

                    // Сериализуем ответ.
                    actionResult.ExecuteResult(ref actionContext);
                }
                else
                // Отправлять результат контроллера будем как есть.
                {
                    Debug.Assert(false);
                    throw new NotImplementedException();
                    // Сериализуем ответ.
                    //serMsg.StatusCode = StatusCode.Ok;

                    //    Debug.Assert(jResponse.ActionMeta != null, "RAW результат может быть только на основе запроса");
                    //    jResponse.ActionMeta.SerializerDelegate(serMsg.MemoryPoolBuffer, responseToSend.ActionResult);
                    //    serMsg.ContentEncoding = jResponse.ActionMeta.ProducesEncoding;
                }
                toDispose = null; // Предотвратить Dispose.
                return buffer;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }
    }
}
