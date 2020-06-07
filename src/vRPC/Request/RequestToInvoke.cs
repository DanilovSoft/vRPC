using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит разобранный запрос с параметрами полученный от удалённой стороны.
    /// </summary>
    [DebuggerDisplay("{" + nameof(ActionMeta) + "}")]
    internal sealed class RequestToInvoke : IDisposable
    {
        private IList<IDisposable>? _disposableArgs;

        /// <summary>
        /// Когда Uid не Null.
        /// </summary>
        public bool IsResponseRequired => Uid != null;

        //[NotNullIfNotNull("IsResponseRequired")]
        public int? Uid { get; }

        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerActionMeta ActionMeta { get; }

        /// <summary>
        /// Аргументы для вызываемого метода.
        /// </summary>
        public object[] Args { get; }

        public RequestToInvoke(int? uid, ControllerActionMeta invokeAction, object[] args, IList<IDisposable> disposableArgs)
        {
            Uid = uid;
            ActionMeta = invokeAction;
            Args = args;
            _disposableArgs = disposableArgs;
        }

        public void Dispose()
        {
            var disposableArgs = Interlocked.Exchange(ref _disposableArgs, null);

            if (disposableArgs != null)
            {
                for (int i = 0; i < disposableArgs.Count; i++)
                {
                    disposableArgs[i].Dispose();
                }
            }
        }
    }
}
