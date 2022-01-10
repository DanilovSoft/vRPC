using System.Threading.Tasks;

namespace DanilovSoft.vRPC.AspNetCore
{
    internal interface IHttpJsonRpcFeature
    {
        bool IsJsonRpcRequest { get; }
        Task<RpcManagedConnection> AcceptAsync(JrpcAcceptContext acceptContext);
    }
}
