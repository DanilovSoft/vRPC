namespace DanilovSoft.vRPC.AspNetCore
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
    using Microsoft.Extensions.DependencyInjection;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

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
