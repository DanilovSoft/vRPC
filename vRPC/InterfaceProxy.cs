using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace vRPC
{
    public class InterfaceProxy : TypeProxy
    {
        private readonly Func<ValueTask<Context>> _contextCallback;
        private readonly string _controllerName;

        public InterfaceProxy((Func<ValueTask<Context>> contextCallback, string controllerName) state)
        {
            _contextCallback = state.contextCallback;
            _controllerName = state.controllerName;
        }

        //[DebuggerStepThrough]
        public override object Invoke(MethodInfo targetMethod, object[] args)
        {
            ValueTask<Context> contextTask = _contextCallback();
            return Context.OnProxyCall(contextTask, targetMethod, args, _controllerName);
        }
    }
}
