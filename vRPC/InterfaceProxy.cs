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
    public class InterfaceProxy : TypeProxy
    {
        private Func<ValueTask<Context>> ContextCallback { get; set; }
        private readonly string _controllerName;

        public InterfaceProxy(string controllerName)
        {
            _controllerName = controllerName;
        }

        public void SetCallback(Func<ValueTask<Context>> contextCallback)
        {
            ContextCallback = contextCallback;
        }

        public InterfaceProxy Clone(Func<ValueTask<Context>> contextCallback)
        {
            var proxy = (InterfaceProxy)MemberwiseClone();
            proxy.SetCallback(contextCallback);
            proxy.ContextCallback = contextCallback;
            return proxy;
        }

        public override object Invoke(MethodInfo targetMethod, object[] args)
        {
            ValueTask<Context> contextTask = ContextCallback();
            return Context.OnProxyCall(contextTask, targetMethod, args, _controllerName);
        }
    }
}
