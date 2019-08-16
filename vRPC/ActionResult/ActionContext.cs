using System.IO;

namespace vRPC
{
    public sealed class ActionContext
    {
        private readonly Context _context;
        internal Stream ResponseStream { get; }
        /// <summary>
        /// Может быть <see langword="null"/>.
        /// </summary>
        internal RequestContext Request { get; }
        public StatusCode StatusCode { get; internal set; }
        internal string ProducesEncoding { get; set; }

        internal ActionContext(Context context, Stream responseStream, RequestContext request)
        {
            _context = context;
            Request = request;
            ResponseStream = responseStream;
        }
    }
}