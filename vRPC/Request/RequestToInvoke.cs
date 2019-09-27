using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит запрос полученный от удалённой стороны.
    /// </summary>
    [DebuggerDisplay("{ActionToInvoke}")]
    internal sealed class RequestToInvoke
    {
        ///// <summary>
        ///// Десериализованный заголовок запроса. Не может быть null.
        ///// </summary>
        //public HeaderDto HeaderDto { get; }

        public ushort? Uid { get; }

        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerAction ActionToInvoke { get; }

        /// <summary>
        /// Аргументы вызываемого метода.
        /// </summary>
        public object[] Args { get; }

        public RequestToInvoke(ushort? uid, ControllerAction invokeAction, object[] args)
        {
            Uid = uid;
            ActionToInvoke = invokeAction;
            Args = args;
        }
    }
}
