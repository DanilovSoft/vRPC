using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Decorator
{
    // Тип должен быть публичным и не запечатанным.
    /// <summary>
    /// От этого класса наследуются динамические типы и пользовательские интерфейсы. 
    /// </summary>
    [DebuggerDisplay(@"\{Proxy to remote controller {ControllerName}, ConnectionState = {_rpcClient}\}")]
    public class ClientInterfaceProxy : IInterfaceProxy, IInterfaceDecorator
    {
        private string _controllerName;
        private RpcClient _rpcClient;
        public string ControllerName => _controllerName;
        public RpcClient Client => _rpcClient;

        // Вызывается через рефлексию.
        public ClientInterfaceProxy()
        {
            // Этот конструктор является базовым для динамически созданного наследника.
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
            return _rpcClient.OnInterfaceMethodCall(targetMethod, args, _controllerName);
        }
    }
}
