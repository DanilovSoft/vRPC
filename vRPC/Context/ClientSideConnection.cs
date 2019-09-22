using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DanilovSoft.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public sealed class ClientSideConnection : ManagedConnection
    {
        internal static readonly LockedDictionary<MethodInfo, RequestToSend> InterfaceMethodsInfo;
        private readonly RpcClient _client;
        private protected override IConcurrentDictionary<MethodInfo, RequestToSend> _interfaceMethods => InterfaceMethodsInfo;

        // ctor.
        static ClientSideConnection()
        {
            InterfaceMethodsInfo = new LockedDictionary<MethodInfo, RequestToSend>();
        }

        // ctor.
        internal ClientSideConnection(RpcClient client, ClientWebSocket ws, ServiceProvider serviceProvider, InvokeActionsDictionary controllers)
            : base(ws.ManagedWebSocket, isServer: false, serviceProvider, controllers)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected override T InnerGetProxy<T>()
        {
            return _client.GetProxy<T>();
        }
    }
}
