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
        public VRpcClient? Client { get; protected set; }
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

        internal void InitializeClone(VRpcClient rpcClient, string? controllerName)
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

            Task<VoidStruct>? pendingRequest = Client.OnClientMethodCall<VoidStruct>(targetMethod, ControllerName, args);

            return pendingRequest ?? Task.CompletedTask;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask EmptyValueTaskInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct>? pendingRequest = Client.OnClientMethodCall<VoidStruct>(targetMethod, ControllerName, args);

            if (pendingRequest != null)
            {
                return new ValueTask(task: pendingRequest);
            }
            else
            {
                return default;
            }
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask<T> ValueTaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T>? pendingRequest = Client.OnClientMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(pendingRequest != null);
            return new ValueTask<T>(task: pendingRequest);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected Task<T> TaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T>? pendingRequest = Client.OnClientMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(pendingRequest != null);
            return pendingRequest;
        }

        // Вызывается через рефлексию — не переименовывать.
        //[DebuggerHidden]
        protected T Invoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T>? pendingRequest = Client.OnClientMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(pendingRequest != null);

            // Результатом может быть исключение.
            T result = pendingRequest.GetAwaiter().GetResult();
            return result;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected void NoResultInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct>? pendingRequest = Client.OnClientMethodCall<VoidStruct>(targetMethod, ControllerName, args);

            if (pendingRequest != null)
            {
                // Результатом может быть исключение.
                pendingRequest.GetAwaiter().GetResult();
            }
        }
    }
}
