namespace DanilovSoft.vRPC.AspNetCore
{
    using DanilovSoft.WebSockets;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Protocol;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Net.Http.Headers;
    using System;
    using System.Collections.Generic;
    using System.IO;
using System.Linq;
using System.Net;
    using System.Runtime.InteropServices.ComTypes;
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class JsonRpcConnection : VrpcManagedConnection
    {
        public JsonRpcConnection(ManagedWebSocket webSocket, bool isServer, ServiceProvider serviceProvider, InvokeActionsDictionary actions) 
            : base(webSocket, isServer, serviceProvider, actions)
        {
        }

        public static async Task<VrpcManagedConnection> AcceptAsync(HttpContext context, IHttpUpgradeFeature feature)
        {
            if (context.Connection.LocalIpAddress == null)
                throw new InvalidOperationException("Не удалось получить LocalIpAddress");

            if (context.Connection.RemoteIpAddress == null)
                throw new InvalidOperationException("Не удалось получить RemoteIpAddress");

            var localEndPoint = new IPEndPoint(context.Connection.LocalIpAddress, context.Connection.LocalPort);
            var remoteEndPoint = new IPEndPoint(context.Connection.RemoteIpAddress, context.Connection.RemotePort);

            // TODO добавить еще свой таймаут.
            CancellationToken cancellationToken = context.RequestAborted;

            // TODO Проверить хедеры запроса.

            string key = context.Request.Headers[HeaderNames.SecWebSocketKey];
            HandshakeHelpers.GenerateResponseHeaders(key, subProtocol: null, context.Response.Headers);

            Stream opaqueTransport = await feature.UpgradeAsync(); // Sets status code to 101
            var stream = new JRpcStream(opaqueTransport, localEndPoint, remoteEndPoint);

            TimeSpan keepAliveInterval = TimeSpan.FromMinutes(2);

            var webSocket = ManagedWebSocket.CreateFromConnectedStream(stream, isServer: true, subprotocol: null, keepAliveInterval);

            var buf = new byte[1024];
            var result = await webSocket.ReceiveAsync(buf, default);

            throw new NotImplementedException();
        }

        public override bool IsAuthenticated => throw new NotImplementedException();

        private protected override bool ActionPermissionCheck(ControllerMethodMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user)
        {
            throw new NotImplementedException();
        }

        private protected override T InnerGetProxy<T>()
        {
            throw new NotImplementedException();
        }
    }
}
