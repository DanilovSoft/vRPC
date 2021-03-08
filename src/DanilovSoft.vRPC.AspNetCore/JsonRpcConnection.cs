namespace DanilovSoft.vRPC.AspNetCore
{
    using DanilovSoft.vRPC.Decorator;
    using DanilovSoft.WebSockets;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Protocol;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Net.Http.Headers;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
using System.Linq;
using System.Net;
    using System.Runtime.InteropServices.ComTypes;
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class JsonRpcConnection : VrpcManagedConnection
    {
        private readonly ProxyCache _proxyCache = new();

        public JsonRpcConnection(ManagedWebSocket webSocket, bool isServer, IServiceProvider serviceProvider, InvokeActionsDictionary actions) 
            : base(webSocket, isServer, serviceProvider, actions)
        {
        }

        public static async Task<VrpcManagedConnection> AcceptAsync(JrpcAcceptContext acceptContext)
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

            Stream opaqueTransport = await acceptContext.Feature.UpgradeAsync(); // Sets status code to 101
            var stream = new JRpcStream(opaqueTransport, localEndPoint, remoteEndPoint);

            TimeSpan keepAliveInterval = TimeSpan.FromMinutes(2);
            var webSocket = ManagedWebSocket.CreateFromConnectedStream(stream, isServer: true, subprotocol: null, keepAliveInterval);

            var rpc = new JsonRpcConnection(webSocket, isServer: true, acceptContext.Context.RequestServices, acceptContext.Controllers);
            return rpc;
        }

        public override bool IsAuthenticated => false;

        private protected override bool ActionPermissionCheck(ControllerMethodMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user)
        {
            // TODO
            permissionError = null;
            user = null;
            return true;
        }

        private protected override T InnerGetProxy<T>() => GetProxy<T>();

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// Полученный экземпляр можно привести к типу <see cref="ServerInterfaceProxy"/>.
        /// Метод является шорткатом для <see cref="GetProxyDecorator"/>
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public T GetProxy<T>() where T : class
        {
            T? proxy = GetProxyDecorator<T>().Proxy;
            Debug.Assert(proxy != null);
            return proxy;
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public ServerInterfaceProxy<T> GetProxyDecorator<T>() where T : class
        {
            return _proxyCache.GetProxyDecorator<T>(this);
        }
    }
}
