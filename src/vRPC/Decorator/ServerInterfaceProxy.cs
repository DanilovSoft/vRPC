using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC.Decorator
{
    /// <summary>
    /// Этот клас наследует пользовательские интерфейсы.
    /// </summary>
    public abstract class ServerInterfaceProxy
    {
        public string? ControllerName { get; protected set; }
        /// <summary>
        /// Никогда не Null.
        /// </summary>
        public RpcManagedConnection? Connection { get; protected set; }
    }

    /// <summary>
    /// Этот клас реализует пользовательские интерфейсы.
    /// </summary>
    /// <remarks>Тип должен быть публичным и не запечатанным.</remarks>
    [DebuggerDisplay(@"\{Proxy to remote controller {ControllerName}, {Connection}\}")]
    public class ServerInterfaceProxy<TIface> : ServerInterfaceProxy, IInterfaceProxy, IInterfaceDecorator<TIface> where TIface : class
    {
        public TIface? Proxy { get; private set; }

        // Вызывается через рефлексию.
        public ServerInterfaceProxy()
        {
            // Этот конструктор является базовым для динамически созданного наследника.
        }

        internal void InitializeClone(string controllerName, RpcManagedConnection connection)
        {
            Proxy = this as TIface;
            ControllerName = controllerName;
            Connection = connection;
        }

        T IInterfaceProxy.Clone<T>()
        {
            var self = MemberwiseClone() as T;
            Debug.Assert(self != null);
            return self;
        }

        /// <exception cref="Exception"/>
        private RequestMethodMeta GetMeta<T>(MethodInfo targetMethod)
        {
            // Метаданные метода.
            var requestMeta = OldServerSideConnection._methodDict.GetOrAdd(targetMethod, (tm, cn) => new RequestMethodMeta(tm, typeof(T), cn), ControllerName);
            return requestMeta;
        }

        #region Асинхронные псевдо-публичные точки входа

        // Вызывается через рефлексию — не переименовывать.
        protected Task EmptyTaskInvoke(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            try
            {
                RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

                if (!methodMeta.IsNotificationRequest)
                {
                    Task<VoidStruct> pendingRequest = Connection.OnServerRequestCall<VoidStruct>(new VRequest<VoidStruct>(methodMeta, args));
                    return pendingRequest;
                }
                else
                {
                    ValueTask task = Connection.OnServerNotificationCall(methodMeta, args);
                    return task.AsTask();
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
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            try
            {
                RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

                if (!methodMeta.IsNotificationRequest)
                {
                    Task<VoidStruct> pendingRequest = Connection.OnServerRequestCall(new VRequest<VoidStruct>(methodMeta, args));
                    return new ValueTask(task: pendingRequest);
                }
                else
                {
                    ValueTask task = Connection.OnServerNotificationCall(methodMeta, args);
                    return task;
                }
            }
            catch (Exception ex)
            {
                return new(Task.FromException(ex));
            }
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask<T?> ValueTaskInvoke<T>(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            try
            {
                RequestMethodMeta methodMeta = GetMeta<T>(targetMethod);

                Task<T?> pendingRequest = Connection.OnServerRequestCall(new VRequest<T>(methodMeta, args));
                return new(task: pendingRequest);
            }
            catch (Exception ex)
            {
                return new(Task.FromException<T?>(ex));
            }
        }

        // Вызывается через рефлексию — не переименовывать.
        protected Task<T?> TaskInvoke<T>(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            try
            {
                RequestMethodMeta methodMeta = GetMeta<T>(targetMethod);

                Task<T?> pendingRequest = Connection.OnServerRequestCall(new VRequest<T>(methodMeta, args));
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
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<T>(targetMethod);

            Task<T?> pendingRequest = Connection.OnServerRequestCall(new VRequest<T>(methodMeta, args));

            // Результатом может быть исключение.
            return pendingRequest.GetAwaiter().GetResult();
        }

        // Вызывается через рефлексию — не переименовывать.
        protected void NoResultInvoke(MethodInfo targetMethod, object?[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            RequestMethodMeta methodMeta = GetMeta<VoidStruct>(targetMethod);

            if (!methodMeta.IsNotificationRequest)
            {
                Task<VoidStruct> pendingRequest = Connection.OnServerRequestCall(new VRequest<VoidStruct>(methodMeta, args));

                // Результатом может быть исключение.
                pendingRequest.GetAwaiter().GetResult();
            }
            else
            {
                ValueTask valueTask = Connection.OnServerNotificationCall(methodMeta, args);
                if (!valueTask.IsCompletedSuccessfully)
                {
                    valueTask.AsTask().GetAwaiter().GetResult();
                }
            }
        }
    }
}
