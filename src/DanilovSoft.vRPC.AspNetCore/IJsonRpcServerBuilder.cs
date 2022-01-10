using Microsoft.Extensions.DependencyInjection;

namespace DanilovSoft.vRPC.AspNetCore
{
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
