using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит разобранный запрос с параметрами полученный от удалённой стороны.
    /// </summary>
    [DebuggerDisplay("{ActionToInvoke}")]
    internal sealed class RequestToInvoke
    {
        public int? Uid { get; }

        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerActionMeta ActionToInvoke { get; }

        /// <summary>
        /// Аргументы для вызываемого метода.
        /// </summary>
        public object[] Args { get; }

        public RequestToInvoke(int? uid, ControllerActionMeta invokeAction, object[] args)
        {
            Uid = uid;
            ActionToInvoke = invokeAction;
            Args = args;
        }
    }
}
