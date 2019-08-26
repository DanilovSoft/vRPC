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
    internal sealed class RequestMessage : Message
    {
        public string ActionName { get; protected set; }
        public Type ReturnType { get; }

        /// <summary>
        /// Конструктор запроса.
        /// </summary>
        public RequestMessage(Type returnType, string actionName, Arg[] args) : base()
        {
            ReturnType = returnType;
            ActionName = actionName;
            Args = new JToken[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Args[i] = args[i].Value;
            }
        }
    }
}
