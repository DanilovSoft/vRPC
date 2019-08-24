using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace vRPC
{
    /// <summary>
    /// Этот клас динамически наследует пользовательский интерфейс.
    /// </summary>
    public class InterfaceProxy : TypeProxy, ICloneable
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

        public override object Invoke(MethodInfo targetMethod, object[] args)
        {
            ValueTask<Context> contextTask = _contextCallback();
            return Context.OnProxyCall(contextTask, targetMethod, args, _controllerName);
        }
    }
}
