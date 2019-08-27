using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using DanilovSoft.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace vRPC
{
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public sealed class ClientSideConnection : ManagedConnection
    {
        internal static readonly LockedDictionary<MethodInfo, string> ProxyMethodName = new LockedDictionary<MethodInfo, string>();
        private readonly Client _client;
        private protected override IConcurrentDictionary<MethodInfo, string> _proxyMethodName => ProxyMethodName;

        internal ClientSideConnection(Client client, ClientWebSocket ws, ServiceProvider serviceProvider, ControllerActionsDictionary controllers)
            : base(ws, isServer: false, serviceProvider, controllers)
        {
            _client = client;
        }

        private protected override void BeforeInvokeController(Controller controller)
        {
            var clientController = (ClientController)controller;
            clientController.Context = _client;
        }

        protected override bool InvokeMethodPermissionCheck(MethodInfo method, Type controllerType, out IActionResult permissionError)
        // Клиент всегда разрешает серверу вызывать методы.
        {
            permissionError = null;
            return true;
        }
    }
}
