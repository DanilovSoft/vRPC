using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит разобранный запрос с параметрами полученный от удалённой стороны.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay(@"\{{" + nameof(ControllerMethod) + @" ?? default}\}")]
    internal readonly struct RequestContext : IDisposable
    {
        /// <summary>
        /// Когда Id не Null.
        /// </summary>
        public bool IsResponseRequired => Id != null;
        public int? Id { get; }

        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerMethodMeta ControllerMethod { get; }

        /// <summary>
        /// Аргументы для вызываемого метода.
        /// </summary>
        public object[] Args { get; }

        public ManagedConnection Context { get; }
        public bool IsJsonRpc { get; }

        // ctor
        public RequestContext(ManagedConnection connection, int? id, ControllerMethodMeta method, object[] args, bool isJsonRpc)
        {
            Debug.Assert(method != null);

            Context = connection;
            Id = id;
            ControllerMethod = method;
            Args = args;
            IsJsonRpc = isJsonRpc;
        }

        public void Dispose()
        {
            ControllerMethod.DisposeArgs(Args);
        }
    }
}
