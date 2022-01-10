using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace DanilovSoft.vRPC.AspNetCore
{
    internal class JrpcAcceptContext
    {
        public virtual string? SubProtocol { get; set; }
        public HttpContext Context { get; }
        public IHttpUpgradeFeature Feature { get; }
        public InvokeActionsDictionary Controllers { get; }

        public JrpcAcceptContext(HttpContext context, IHttpUpgradeFeature feature, InvokeActionsDictionary controllers)
        {
            Context = context;
            Feature = feature;
            Controllers = controllers;
        }
    }
}
