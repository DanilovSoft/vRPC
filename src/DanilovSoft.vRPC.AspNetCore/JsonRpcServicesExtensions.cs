namespace Microsoft.Extensions.DependencyInjection
{
using DanilovSoft.vRPC.AspNetCore;
    using DanilovSoft.vRPC.AspNetCore.Internal;
    using DanilovSoft.vRPC.AspNetCore.Model.Internal;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static class JsonRpcServicesExtensions
    {
        public static IJsonRpcServerBuilder AddJsonRpc(this IServiceCollection services)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));
            
            services.AddRouting();
            services.AddOptions();
            services.TryAddSingleton<JrpcMarkerService>();

            // Model
            services.TryAddSingleton(typeof(ServiceRouteBuilder));

            return new JrpcServerBuilder(services);
        }
    }
}
