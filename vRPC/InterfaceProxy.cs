using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace vRPC
{
    /// <summary>
    /// От этого класса наследуются динамические типы и пользовательский интерфейс. Поэтому должен быть публичным и не запечатанным.
    /// </summary>
    public class InterfaceProxy : ICloneable
    {
        private Func<ValueTask<Context>> _contextCallback;
        private string _controllerName;

        public InterfaceProxy()
        {
            
        }

        public void SetCallback(string controllerName, Func<ValueTask<Context>> contextCallback)
        {
            _controllerName = controllerName;
            _contextCallback = contextCallback;
        }

        object ICloneable.Clone() => Clone();
        public InterfaceProxy Clone()
        {
            var proxy = (InterfaceProxy)MemberwiseClone(); // Не вызывает конструктор.
            return proxy;
        }

        protected object Invoke(MethodInfo targetMethod, object[] args)
        {
            ValueTask<Context> contextTask = _contextCallback();
            return Context.OnProxyCall(contextTask, targetMethod, args, _controllerName);
        }
    }
}
