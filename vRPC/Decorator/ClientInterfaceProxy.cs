using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Decorator
{
    /// <summary>
    /// От этого класса наследуются динамические типы и пользовательские интерфейсы. 
    /// Поэтому должен быть публичным и не запечатанным.
    /// </summary>
    [DebuggerDisplay(@"\{Proxy for remote calling controller {ControllerName}, {_rpcClient}\}")]
    public class ClientInterfaceProxy : IInterfaceProxy, IInterfaceDecorator
    {
        private string _controllerName;
        private RpcClient _rpcClient;
        public string ControllerName => _controllerName;
        public RpcClient Client => _rpcClient;

        public ClientInterfaceProxy()
        {
            
        }

        internal void InitializeClone(RpcClient rpcClient, string controllerName)
        {
            _rpcClient = rpcClient;
            _controllerName = controllerName;
        }

        T IInterfaceProxy.Clone<T>()
        {
            return (T)MemberwiseClone();
        }

        // Вызывается через рефлексию.
        protected object Invoke(MethodInfo targetMethod, object[] args)
        {
            // Начать соединение или взять существующее.
            ValueTask<ManagedConnection> contextTask = _rpcClient.GetConnectionForInterfaceCallback(); // _getConnectionCallback();

            return ManagedConnection.OnClientProxyCallStatic(contextTask, targetMethod, args, _controllerName);
        }
    }
}
