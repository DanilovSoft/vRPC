namespace DanilovSoft.vRPC.AspNetCore
{
    using Microsoft.AspNetCore.Http;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading.Tasks;

    internal interface IHttpJsonRpcFeature
    {
        bool IsJsonRpcRequest { get; }
        Task<VrpcManagedConnection> AcceptAsync(JrpcAcceptContext acceptContext);
    }
}
