using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Decorator
{
    /// <summary>
    /// От этого класса наследуются динамические типы и пользовательские интерфейсы.
    /// Поэтому должен быть публичным и не запечатанным.
    /// </summary>
    [DebuggerDisplay(@"\{Proxy for remote calling controller {ControllerName}, {_connection}\}")]
    public class ServerInterfaceProxy : IInterfaceDecorator, IInterfaceProxy
    {
        private ManagedConnection _connection;
        private string _controllerName;
        public string ControllerName => _controllerName;
        public ManagedConnection Connection => _connection;

        public ServerInterfaceProxy()
        {

        }

        internal void InitializeClone(string controllerName, ManagedConnection connection)
        {
            _controllerName = controllerName;
            _connection = connection;
        }

        T IInterfaceProxy.Clone<T>()
        {
            return (T)MemberwiseClone();
        }

        // Вызывается через рефлексию.
        protected object Invoke(MethodInfo targetMethod, object[] args)
        {
            return _connection.OnServerProxyCall(targetMethod, args, _controllerName);
        }
    }
}
