using System;
using System.Diagnostics;
using System.Reflection;
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
    public class ClientInterfaceProxy<TIface> : ClientInterfaceProxy, IInterfaceProxy, IInterfaceDecorator<TIface> where TIface : class
    {
        public TIface? Proxy { get; private set; }

        // Вызывается через рефлексию.
        public ClientInterfaceProxy()
        {
            
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TResult">Возвращаемый тип метода.</typeparam>
        /// <param name="targetMethod"></param>
        /// <exception cref="Exception">Могут быть неизвестные исключения.</exception>
        private RequestMethodMeta GetMeta<TResult>(MethodInfo targetMethod)
        {
            // Метаданные запроса.
            var methodMeta = ClientSideConnection.MethodDict.GetOrAdd(targetMethod, 
                (tm, cn) => new RequestMethodMeta(tm, typeof(TResult), cn), ControllerName);

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

        #region Асинхронные псевдо-публичные точки входа

        // Вызывается через рефлексию — не переименовывать.
        protected Task EmptyTaskInvoke(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            try
            {
                RequestMethodMeta method = GetMeta<VoidStruct>(targetMethod);

                if (!method.IsNotificationRequest)
                {
                    Task<VoidStruct> task = Client.OnClientMethodCall<VoidStruct>(method, args);
                    return task;
                }
                else
                {
                    ValueTask valueTask = Client.OnClientNotificationCall(method, args);
                    return valueTask.AsTask();
                }
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask EmptyValueTaskInvoke(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> pendingRequest;
            try
            {
                RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

                if (!methodMeta.IsNotificationRequest)
                {
                    pendingRequest = Client.OnClientMethodCall<VoidStruct>(methodMeta, args);
                }
                else
                {
                    ValueTask valueTask = Client.OnClientNotificationCall(methodMeta, args);
                    return valueTask;
                }
            }
            catch (Exception ex)
            {
                return new(Task.FromException(ex));
            }
            return new(task: pendingRequest);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask<T?> ValueTaskInvoke<T>(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            Task<T?> pendingRequest;
            try
            {
                RequestMethodMeta methodMeta = GetMeta<T>(targetMethod);
                pendingRequest = Client.OnClientMethodCall<T>(methodMeta, args);
            }
            catch (Exception ex)
            {
                return new(Task.FromException<T?>(ex));
            }
            return new(task: pendingRequest);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected Task<T?> TaskInvoke<T>(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            try
            {
                RequestMethodMeta methodMeta = GetMeta<T>(targetMethod);
                var pendingRequest = Client.OnClientMethodCall<T>(methodMeta, args);
                return pendingRequest;
            }
            catch (Exception ex)
            {
                return Task.FromException<T?>(ex);
            }
        }

        #endregion

        // Вызывается через рефлексию — не переименовывать.
        protected T? Invoke<T>(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Client != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<T>(targetMethod);
            
            Task<T?> pendingRequest = Client.OnClientMethodCall<T>(methodMeta, args);

            // Результатом может быть исключение.
            T? result = pendingRequest.GetAwaiter().GetResult();

            return result!;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected void NoResultInvoke(MethodInfo targetMethod, object?[] args)
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
                if (valueTask.IsCompletedSuccessfully)
                {
                    // для ValueTask нужно всегда забирать результат.
                    valueTask.GetAwaiter().GetResult();
                }
                else
                {
                    valueTask.AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }
}
