namespace Microsoft.AspNetCore.Builder
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public sealed class JrpcServiceEndpointConventionBuilder : IEndpointConventionBuilder
    {
        internal JrpcServiceEndpointConventionBuilder(List<IEndpointConventionBuilder> endpointConventionBuilders)
        {

        }

        public void Add(Action<EndpointBuilder> convention)
        {
            
        }
    }
}
