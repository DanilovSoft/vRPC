namespace DanilovSoft.vRPC.AspNetCore.Internal
{
    using Microsoft.Extensions.DependencyInjection;
    using System;
    using System.Collections.Generic;
    using System.Text;

    internal class JrpcServerBuilder : IJsonRpcServerBuilder
    {
        public IServiceCollection Services { get; }

        public JrpcServerBuilder(IServiceCollection services)
        {
            Services = services;
        }
    }
}
