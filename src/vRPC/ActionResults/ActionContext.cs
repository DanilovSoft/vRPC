using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Контекст запроса.
    /// </summary>
    public sealed class ActionContext
    {
        internal Stream ResponseStream { get; }
        /// <summary>
        /// Может быть <see langword="null"/> если не удалось разобрать запрос.
        /// </summary>
        internal ControllerActionMeta? ActionMeta { get; }
        public StatusCode StatusCode { get; internal set; }
        internal string? ProducesEncoding { get; set; }

        //[DebuggerStepThrough]
        internal ActionContext(ControllerActionMeta? actionMeta, Stream responseStream)
        {
            ActionMeta = actionMeta;
            ResponseStream = responseStream;
        }
    }
}