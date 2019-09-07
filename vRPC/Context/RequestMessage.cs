using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    /// <summary>
    /// Сообщение для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Request: {ActionName,nq}\}")]
    internal sealed class RequestMessage : IMessage
    {
        public string ActionName { get; }
        public Type ReturnType { get; }
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public JToken[] Args { get; }

        /// <summary>
        /// Конструктор запроса.
        /// </summary>
        public RequestMessage(Type returnType, string actionName, object[] args)
        {
            ReturnType = returnType;
            ActionName = actionName;
            Args = new JToken[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                object arg = args[i];
                Args[i] = arg == null ? null : JToken.FromObject(arg);
            }
        }
    }
}
