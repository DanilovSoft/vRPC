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

        private RequestMethodMeta GetMeta<TResult>(MethodInfo targetMethod)
        {
            // Метаданные запроса.
            var methodMeta = ClientSideConnection.MethodDict.GetOrAdd(targetMethod, (tm, cn) => new RequestMethodMeta(tm, typeof(TResult), cn), ControllerName);

            return methodMeta;
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

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            if (!methodMeta.IsNotificationRequest)
            {
                Task<VoidStruct> task = Client.OnClientMethodCall<VoidStruct>(methodMeta, args);
                return task;
            }
            else
            {
                ValueTask valueTask = Client.OnClientNotificationCall(methodMeta, args);
                return valueTask.AsTask();
            }
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask EmptyValueTaskInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            if (!methodMeta.IsNotificationRequest)
            {
                Task<VoidStruct> pendingRequest = Client.OnClientMethodCall<VoidStruct>(methodMeta, args);
                return new ValueTask(task: pendingRequest);
            }
            else
            {
                ValueTask valueTask = Client.OnClientNotificationCall(methodMeta, args);
                return valueTask;
            }
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask<T> ValueTaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            Task<T> pendingRequest = Client.OnClientMethodCall<T>(methodMeta, args);
            return new ValueTask<T>(task: pendingRequest);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected Task<T> TaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            Task<T> pendingRequest = Client.OnClientMethodCall<T>(methodMeta, args);
            return pendingRequest;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected T Invoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            Task<T> pendingRequest = Client.OnClientMethodCall<T>(methodMeta, args);

            // Результатом может быть исключение.
            T result = pendingRequest.GetAwaiter().GetResult();
            return result;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected void NoResultInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            if (!methodMeta.IsNotificationRequest)
            {
                Task<VoidStruct> pendingRequest = Client.OnClientMethodCall<VoidStruct>(methodMeta, args);

                // Результатом может быть исключение.
                pendingRequest.GetAwaiter().GetResult();
            }
            else
            {
                ValueTask valueTask = Client.OnClientNotificationCall(methodMeta, args);
                if (!valueTask.IsCompletedSuccessfully)
                {
                    valueTask.AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }
}
