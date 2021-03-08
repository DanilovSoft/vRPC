namespace DanilovSoft.vRPC.AspNetCore
{
    using Microsoft.Extensions.DependencyInjection;
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// A builder abstraction for configuring gRPC servers.
    /// </summary>
    public interface IJsonRpcServerBuilder
    {
        /// <summary>
        /// Gets the builder service collection.
        /// </summary>
        IServiceCollection Services { get; }
    }
}
