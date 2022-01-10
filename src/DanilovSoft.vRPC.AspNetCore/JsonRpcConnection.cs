using DanilovSoft.WebSockets;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.AspNetCore
{
    internal sealed class JsonRpcConnection : RpcManagedConnection
    {
        public JsonRpcConnection(ManagedWebSocket webSocket, bool isServer, IServiceProvider serviceProvider, InvokeActionsDictionary actions) 
            : base(webSocket, isServer, serviceProvider, actions)
        {
        }

        public override bool IsAuthenticated => false;

        public static async Task<JsonRpcConnection> AcceptAsync(JrpcAcceptContext acceptContext)
        {
            if (acceptContext.Context.Connection.LocalIpAddress == null)
                throw new InvalidOperationException("Не удалось получить LocalIpAddress");

            if (acceptContext.Context.Connection.RemoteIpAddress == null)
                throw new InvalidOperationException("Не удалось получить RemoteIpAddress");

            var localEndPoint = new IPEndPoint(acceptContext.Context.Connection.LocalIpAddress, acceptContext.Context.Connection.LocalPort);
            var remoteEndPoint = new IPEndPoint(acceptContext.Context.Connection.RemoteIpAddress, acceptContext.Context.Connection.RemotePort);

            // TODO добавить еще свой таймаут.
            CancellationToken cancellationToken = acceptContext.Context.RequestAborted;

            // TODO Проверить хедеры запроса.

            string key = acceptContext.Context.Request.Headers[HeaderNames.SecWebSocketKey];
            HandshakeHelpers.GenerateResponseHeaders(key, subProtocol: null, acceptContext.Context.Response.Headers);

            // Установит статус 101 и отправит хедеры.
            Stream opaqueTransport = await acceptContext.Feature.UpgradeAsync();

            var stream = new JRpcStream(opaqueTransport, localEndPoint, remoteEndPoint);

            TimeSpan keepAliveInterval = TimeSpan.FromMinutes(2);
            var webSocket = ManagedWebSocket.CreateFromConnectedStream(stream, isServer: true, subprotocol: null, keepAliveInterval);

            var rpc = new JsonRpcConnection(webSocket, isServer: true, acceptContext.Context.RequestServices, acceptContext.Controllers);
            return rpc;
        }

        private protected override bool ActionPermissionCheck(ControllerMethodMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user)
        {
            // TODO
            permissionError = null;
            user = null;
            return true;
        }
    }
}
