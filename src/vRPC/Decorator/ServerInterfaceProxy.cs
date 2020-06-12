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
        public ManagedConnection? Connection { get; protected set; }
    }

    /// <summary>
    /// Этот клас реализует пользовательские интерфейсы.
    /// </summary>
    /// <remarks>Тип должен быть публичным и не запечатанным.</remarks>
    [DebuggerDisplay(@"\{Proxy to remote controller {ControllerName}, {Connection}\}")]
    [SuppressMessage("Design", "CA1062:Проверить аргументы или открытые методы", Justification = "Логически не может быть Null")]
    public class ServerInterfaceProxy<TIface> : ServerInterfaceProxy, IInterfaceProxy, IInterfaceDecorator<TIface> where TIface : class
    {
        public TIface? Proxy { get; private set; }

        // Вызывается через рефлексию.
        public ServerInterfaceProxy()
        {
            // Этот конструктор является базовым для динамически созданного наследника.
        }

        internal void InitializeClone(string controllerName, ManagedConnection connection)
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

        // Вызывается через рефлексию — не переименовывать.
        protected Task EmptyTaskInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> task = Connection.OnInterfaceMethodCall<VoidStruct>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return task;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask EmptyValueTaskInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> task = Connection.OnInterfaceMethodCall<VoidStruct>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return new ValueTask(task: task);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected ValueTask<T> ValueTaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            Task<T> task = Connection.OnInterfaceMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return new ValueTask<T>(task: task);
        }

        // Вызывается через рефлексию — не переименовывать.
        protected Task<T> TaskInvoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            Task<T> task = Connection.OnInterfaceMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);
            return task;
        }

        // Вызывается через рефлексию — не переименовывать.
        protected T Invoke<T>(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            Task<T> task = Connection.OnInterfaceMethodCall<T>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);

            // Результатом может быть исключение.
            return task.GetAwaiter().GetResult();
        }

        // Вызывается через рефлексию — не переименовывать.
        protected void NoResultInvoke(MethodInfo targetMethod, object[] args)
        {
            Debug.Assert(Connection != null);
            Debug.Assert(targetMethod != null);

            Task<VoidStruct> task = Connection.OnInterfaceMethodCall<VoidStruct>(targetMethod, ControllerName, args);
            Debug.Assert(task != null);

            // Результатом может быть исключение.
            task.GetAwaiter().GetResult();
        }
    }
}
