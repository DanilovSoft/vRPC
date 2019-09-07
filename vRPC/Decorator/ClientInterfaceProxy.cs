using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace vRPC.Decorator
{
    /// <summary>
    /// От этого класса наследуются динамические типы и пользовательский интерфейс. 
    /// Поэтому должен быть публичным и не запечатанным.
    /// </summary>
    public class ClientInterfaceProxy : ICloneable, IInterfaceProxy
    {
        private Func<ValueTask<ManagedConnection>> _contextCallback;
        private string _controllerName;

        public ClientInterfaceProxy()
        {
            
        }

        public void Initialize(string controllerName, Func<ValueTask<ManagedConnection>> contextCallback)
        {
            _controllerName = controllerName;
            _contextCallback = contextCallback;
        }

        object ICloneable.Clone() => ((IInterfaceProxy)this).Clone<ClientInterfaceProxy>();

        T IInterfaceProxy.Clone<T>()
        {
            return (T)MemberwiseClone();
        }

        protected object Invoke(MethodInfo targetMethod, object[] args)
        {
            ValueTask<ManagedConnection> contextTask = _contextCallback();
            return ManagedConnection.OnClientProxyCallStatic(contextTask, targetMethod, args, _controllerName);
        }
    }
}
