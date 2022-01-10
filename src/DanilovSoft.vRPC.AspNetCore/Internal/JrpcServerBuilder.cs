using Microsoft.Extensions.DependencyInjection;

namespace DanilovSoft.vRPC.AspNetCore.Internal
{
    internal class JrpcServerBuilder : IJsonRpcServerBuilder
    {
        public IServiceCollection Services { get; }

        public JrpcServerBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}
