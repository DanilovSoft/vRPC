using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DanilovSoft.vRPC
{
    public sealed class ActionContext
    {
        internal Stream ResponseStream { get; }
        /// <summary>
        /// Не может быть <see langword="null"/>.
        /// </summary>
        internal RequestToInvoke RequestContext { get; }
        public StatusCode StatusCode { get; internal set; }
        internal string ProducesEncoding { get; set; }

        [DebuggerStepThrough]
        internal ActionContext(in RequestToInvoke requestContext, Stream responseStream)
        {
            RequestContext = requestContext;
            ResponseStream = responseStream;
        }
    }
}