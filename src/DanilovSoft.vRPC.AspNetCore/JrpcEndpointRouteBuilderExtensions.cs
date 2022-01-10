using DanilovSoft.vRPC.AspNetCore;
using DanilovSoft.vRPC.AspNetCore.Model.Internal;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.AspNetCore.Builder
{
    public static class JrpcEndpointRouteBuilderExtensions
    {
        public static JrpcServiceEndpointConventionBuilder MapJsonRpcService(this IEndpointRouteBuilder builder, string pattern)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            ValidateServicesRegistered(builder.ServiceProvider);

            var serviceRouteBuilder = builder.ServiceProvider.GetRequiredService<ServiceRouteBuilder>();
            var endpointConventionBuilders = serviceRouteBuilder.Build(pattern, builder);

            return new JrpcServiceEndpointConventionBuilder(endpointConventionBuilders);
        }

        private static void ValidateServicesRegistered(IServiceProvider serviceProvider)
        {
            var marker = serviceProvider.GetService(typeof(JrpcMarkerService));
            if (marker == null)
            {
                throw new InvalidOperationException("Unable to find the required services. Please add all the required services by calling " +
                    "'IServiceCollection.AddJsonRpc' inside the call to 'ConfigureServices(...)' in the application startup code.");
            }
        }
    }
}
