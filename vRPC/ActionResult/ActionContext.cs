using System.IO;

namespace vRPC
{
    public sealed class ActionContext
    {
        internal Stream ResponseStream { get; }
        /// <summary>
        /// Может быть <see langword="null"/>.
        /// </summary>
        internal RequestContext Request { get; }
        public StatusCode StatusCode { get; internal set; }
        internal string ProducesEncoding { get; set; }

        internal ActionContext(Stream responseStream, RequestContext request)
        {
            Request = request;
            ResponseStream = responseStream;
        }
    }
}