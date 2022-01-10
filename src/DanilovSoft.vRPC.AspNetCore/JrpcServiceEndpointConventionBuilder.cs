using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Builder
{
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
