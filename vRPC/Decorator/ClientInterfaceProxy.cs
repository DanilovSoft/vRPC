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
        public string? ControllerName { get; protected set; }
        public RpcClient? Client { get; protected set; }
    }

    // Тип должен быть публичным и не запечатанным.
    /// <summary>
    /// Этот клас наследует пользовательские интерфейсы.
    /// </summary>
    [DebuggerDisplay(@"\{Proxy to remote controller {ControllerName}, ConnectionState = {Client}\}")]
    public class ClientInterfaceProxy<TIface> : ClientInterfaceProxy, IInterfaceProxy, IInterfaceDecorator<TIface> where TIface : class
    {
        public TIface? Proxy { get; private set; }

        // Вызывается через рефлексию.
        public ClientInterfaceProxy()
        {
            
        }

        internal void InitializeClone(RpcClient rpcClient, string? controllerName)
        {
            Proxy = this as TIface;
            Client = rpcClient;
            ControllerName = controllerName;
        }

        T IInterfaceProxy.Clone<T>()
        {
            var self = MemberwiseClone() as T;
            Debug.Assert(self != null);
            return self;
        }


        // Вызывается через рефлексию.
        /// <returns>Может быть незавершённый таск или RAW результат или Null.</returns>
        [SuppressMessage("Design", "CA1062:Проверить аргументы или открытые методы", Justification = "Логически не может быть Null")]
        protected object? Invoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            // Может вернуть незавершённый таск.
            object? returnValue = Client.OnInterfaceMethodCall(targetMethod, args, ControllerName);

            DebugOnly.ValidateIsInstanceOfType(returnValue, targetMethod.ReturnType);

            return returnValue;
        }
    }
}
