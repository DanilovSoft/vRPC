namespace DanilovSoft.vRPC.AspNetCore.Model.Internal
{
    using DanilovSoft.vRPC.AspNetCore.Internal;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Primitives;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipelines;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal class ServiceRouteBuilder
    {
        private readonly InvokeActionsDictionary _controllers;

        public ServiceRouteBuilder(InvokeActionsDictionary controllers)
        {
            _controllers = controllers;
        }

        internal List<IEndpointConventionBuilder> Build(string pattern, IEndpointRouteBuilder endpointRouteBuilder)
        {
            var endpointConventionBuilders = new List<IEndpointConventionBuilder>();

            var endpointBuilder = endpointRouteBuilder.Map(pattern, HandleRequest);

            //endpointBuilder.Add(ep =>
            //{
            //    ep.DisplayName = $"vRPC - {method.Pattern.RawText}";

            //    ep.Metadata.Add(new VrpcMethodMetadata(typeof(TService), method.Method));
            //    foreach (var item in method.Metadata)
            //    {
            //        ep.Metadata.Add(item);
            //    }
            //});

            return endpointConventionBuilders;
        }

        private async Task HandleRequest(HttpContext context)
        {
            var feature = context.Features.Get<IHttpUpgradeFeature>();
            if (!feature.IsUpgradableRequest)
            {
                return;
            }

            VrpcManagedConnection rpcConnection = await JsonRpcConnection.AcceptAsync(new JrpcAcceptContext(context, feature, _controllers));

            rpcConnection.StartReceiveSendLoop();

            var closeReason = await rpcConnection.Completion;
        }
    }
}
