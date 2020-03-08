using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Описывает метод который требуется вызвать на удалённой стороне.
    /// Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay(@"\{Request = {ActionName}\}")]
    internal sealed class RequestMeta : IMessageMeta
    {
        public Type ReturnType { get; }
        /// <summary>
        /// Инкапсулированный в Task тип результата функции.
        /// </summary>
        public Type IncapsulatedReturnType { get; }
        /// <summary>
        /// True если метод интерфейса был помечен атрибутом <see cref="NotificationAttribute"/> 
        /// и соответственно не возвращает результат.
        /// </summary>
        public bool IsNotificationRequest { get; }
        /// <summary>
        /// Возвращает <see langword="true"/> если функция имеет возвращаемый тип <see cref="Task"/> (<see cref="Task{TResult}"/>)
        /// или <see cref="ValueTask"/> (<see cref="ValueTask{TResult}"/>).
        /// </summary>
        public bool IsAsync { get; }
        /// <summary>
        /// Имя метода например 'Home/Hello' без постфикса 'Async'.
        /// </summary>
        public string ActionName { get; }
        public bool IsRequest => true;

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="interfaceMethod"></param>
        /// <param name="controllerName"></param>
        public RequestMeta(MethodInfo interfaceMethod, string controllerName)
        {
            ReturnType = interfaceMethod.ReturnType;

            IsNotificationRequest = Attribute.IsDefined(interfaceMethod, typeof(NotificationAttribute));

            if (IsNotificationRequest)
            {
                ValidateNotification(interfaceMethod.ReturnType, interfaceMethod.Name);
            }
            IncapsulatedReturnType = GetMethodReturnType(interfaceMethod.ReturnType);
            ActionName = $"{controllerName}{GlobalVars.ControllerNameSplitter}{interfaceMethod.GetNameTrimAsync()}";
            IsAsync = interfaceMethod.ReturnType.IsAsyncReturnType();
        }

        public RequestMeta(string controllerName, string methodName, Type returnType, bool notification)
        {
            ReturnType = returnType;
            IsNotificationRequest = notification;

            if (notification)
            {
                ValidateNotification(returnType, methodName);
            }
            IncapsulatedReturnType = GetMethodReturnType(returnType);
            ActionName = $"{controllerName}{GlobalVars.ControllerNameSplitter}{methodName}";
            IsAsync = returnType.IsAsyncReturnType();
        }

        private static void ValidateNotification(Type returnType, string methodName)
        {
            if (returnType != typeof(void) && returnType != typeof(Task) && returnType != typeof(ValueTask))
            {
                throw new InvalidOperationException($"Метод '{methodName}' помечен атрибутом [Notification] поэтому " +
                    $"возвращаемый тип метода может быть только void или Task или ValueTask.");
            }
        }

        /// <summary>
        /// Возвращает инкапсулированный в <see cref="Task"/> тип результата функции.
        /// </summary>
        private static Type GetMethodReturnType(Type returnType)
        {
            // Если возвращаемый тип функции — Task.
            if (returnType.IsAsyncReturnType())
            {
                // Если у задачи есть результат.
                if (returnType.IsGenericType)
                {
                    // Тип результата задачи.
                    Type resultType = returnType.GenericTypeArguments[0];
                    return resultType;
                }
                else
                {
                    // Возвращаемый тип Task(без результата).
                    return typeof(void);
                }
            }
            else
            // Была вызвана синхронная функция.
            {
                return returnType;
            }
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        public BinaryMessageToSend SerializeRequest(object[] args)
        {
            var serializedMessage = new BinaryMessageToSend(this);
            try
            {
                var request = new RequestMessageDto(ActionName, args);
                ExtensionMethods.SerializeObjectJson(serializedMessage.MemPoolStream, request);
                var ret = serializedMessage;
                serializedMessage = null;
                return ret;
            }
            finally
            {
                serializedMessage?.Dispose();
            }
        }
    }
}
