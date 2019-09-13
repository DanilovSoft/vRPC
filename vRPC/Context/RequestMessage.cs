using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Запрос для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Request: {ActionName,nq}\}")]
    internal sealed class RequestMessage : IMessage
    {
        /// <summary>
        /// Имя вызываемого метода вместе с контроллером например 'Home/Hello'.
        /// </summary>
        public string ActionName { get; }
        //public Type ReturnType { get; }
        public InterfaceMethodInfo InterfaceMethod { get; }
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public JToken[] Args { get; }

        /// <summary>
        /// Запрос для передачи удаленной стороне. Не подлежит сериализации.
        /// </summary>
        public RequestMessage(InterfaceMethodInfo interfaceMethod, string actionName, object[] args)
        {
            InterfaceMethod = interfaceMethod;
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
