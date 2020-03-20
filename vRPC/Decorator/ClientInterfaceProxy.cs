using DynamicMethodsLib;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Decorator
{
    /// <summary>
    /// Этот клас наследует пользовательские интерфейсы.
    /// </summary>
    public abstract class ClientInterfaceProxy
    {
        public string ControllerName { get; protected set; }
        public RpcClient Client { get; protected set; }
    }

    // Тип должен быть публичным и не запечатанным.
    /// <summary>
    /// Этот клас наследует пользовательские интерфейсы.
    /// </summary>
    [DebuggerDisplay(@"\{Proxy to remote controller {ControllerName}, ConnectionState = {Client}\}")]
    public class ClientInterfaceProxy<TIface> : ClientInterfaceProxy, IInterfaceProxy, IInterfaceDecorator<TIface> where TIface : class
    {
        public TIface Proxy { get; private set; }

        // Вызывается через рефлексию.
        public ClientInterfaceProxy()
        {
            
        }

        internal void InitializeClone(RpcClient rpcClient, string controllerName)
        {
            Proxy = this as TIface;
            Client = rpcClient;
            ControllerName = controllerName;
        }

        T IInterfaceProxy.Clone<T>()
        {
            return MemberwiseClone() as T;
        }


        // Вызывается через рефлексию.
        [SuppressMessage("Design", "CA1062:Проверить аргументы или открытые методы", Justification = "Логически не может быть Null")]
        protected object Invoke(MethodInfo targetMethod, object[] args)
        {
            object returnValue = Client.OnInterfaceMethodCall(targetMethod, args, ControllerName);

            DebugOnly.ValidateReturnType(targetMethod.ReturnType, returnValue);

            return returnValue;
        }
    }
}
