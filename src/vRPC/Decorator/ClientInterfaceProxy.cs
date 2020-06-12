using DanilovSoft.vRPC.Source;
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

    /// <summary>
    /// Этот клас реализует пользовательские интерфейсы.
    /// </summary>
    /// <remarks>Тип должен быть публичным и не запечатанным.</remarks>
    [DebuggerDisplay(@"\{Proxy to remote controller {ControllerName}, ConnectionState = {Client}\}")]
    [SuppressMessage("Design", "CA1062:Проверить аргументы или открытые методы", Justification = "Логически не может быть Null")]
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

        // Вызывается через рефлексию — не переименовывать.
        protected Task EmptyTaskInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> task = Client.OnInterfaceMethodCall<VoidStruct>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return task;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask EmptyValueTaskInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> task = Client.OnInterfaceMethodCall<VoidStruct>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return new ValueTask(task: task);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask<T> ValueTaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T> task = Client.OnInterfaceMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return new ValueTask<T>(task: task);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected Task<T> TaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T> task = Client.OnInterfaceMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return task;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected T Invoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T> task = Client.OnInterfaceMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);

            // Результатом может быть исключение.
            return task.GetAwaiter().GetResult();
        }

        // Вызывается через рефлексию — не переименовывать.
        protected void NoResultInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> task = Client.OnInterfaceMethodCall<VoidStruct>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);

            // Результатом может быть исключение.
            task.GetAwaiter().GetResult();
        }
    }
}
