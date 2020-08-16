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
    [DebuggerDisplay(@"\{{" + nameof(ControllerActionMeta) + @" ?? default}\}")]
    internal readonly struct RequestContext : IDisposable
    {
        /// <summary>
        /// Когда Uid не Null.
        /// </summary>
        public bool IsResponseRequired => Uid != null;
        public int? Uid { get; }

        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerActionMeta ControllerActionMeta { get; }

        /// <summary>
        /// Аргументы для вызываемого метода.
        /// </summary>
        public object[] Args { get; }

        // ctor
        public RequestContext(int? uid, ControllerActionMeta controllerActionMeta, object[] args)
        {
            Uid = uid;
            ControllerActionMeta = controllerActionMeta;
            Args = args;
        }

        public void Dispose()
        {
            ControllerActionMeta.DisposeArgs(Args);
        }
    }

    ///// <summary>
    ///// Содержит разобранный запрос с параметрами полученный от удалённой стороны.
    ///// </summary>
    //[StructLayout(LayoutKind.Auto)]
    //[DebuggerDisplay("{" + nameof(ControllerActionMeta) + "}")]
    //internal sealed class RequestContext : IDisposable
    //{
    //    private IList<IDisposable>? _disposableArgs;

    //    /// <summary>
    //    /// Когда Uid не Null.
    //    /// </summary>
    //    public bool IsResponseRequired => Uid != null;
    //    public int? Uid { get; }

    //    /// <summary>
    //    /// Запрашиваемый метод контроллера.
    //    /// </summary>
    //    public ControllerActionMeta ControllerActionMeta { get; }

    //    /// <summary>
    //    /// Аргументы для вызываемого метода.
    //    /// </summary>
    //    public object[] Args { get; }

    //    public RequestContext(int? uid, ControllerActionMeta invokeAction, object[] args, IList<IDisposable> disposableArgs)
    //    {
    //        Uid = uid;
    //        ControllerActionMeta = invokeAction;
    //        Args = args;
    //        _disposableArgs = disposableArgs;
    //    }

    //    public void Dispose()
    //    {
    //        var disposableArgs = Interlocked.Exchange(ref _disposableArgs, null);

    //        if (disposableArgs != null)
    //        {
    //            for (int i = 0; i < disposableArgs.Count; i++)
    //            {
    //                disposableArgs[i].Dispose();
    //            }
    //        }
    //    }
    //}
}
