using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Decorator
{
    /// <summary>
    /// От этого класса наследуются динамические типы и пользовательский интерфейс. Поэтому должен быть публичным и не запечатанным.
    /// </summary>
    public class ServerInterfaceProxy : IInterfaceProxy
    {
        private ManagedConnection _connection;
        private string _controllerName;

        public ServerInterfaceProxy()
        {

        }

        public void Initialize(string controllerName, ManagedConnection connection)
        {
            _controllerName = controllerName;
            _connection = connection;
        }

        T IInterfaceProxy.Clone<T>()
        {
            return (T)MemberwiseClone();
        }

        protected object Invoke(MethodInfo targetMethod, object[] args)
        {
            return _connection.OnServerProxyCall(targetMethod, args, _controllerName);
        }
    }
}
