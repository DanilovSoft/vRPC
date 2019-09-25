using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Запрос для передачи удаленной стороне.
    /// Создаётся при вызове метода через интерфейс.
    /// Не подлежит сериализации.
    /// </summary>
    internal sealed class RequestToSend : IMessage
    {
        public MethodInfo Method { get; }
        /// <summary>
        /// Инкапсулированный в Task тип результата функции.
        /// </summary>
        public Type IncapsulatedReturnType { get; }
        /// <summary>
        /// True если метод интерфейса был помечен атрибутом <see cref="NotificationAttribute"/> 
        /// и соответственно не возвращает результат.
        /// </summary>
        public bool Notification { get; }
        public bool IsAsync { get; }
        /// <summary>
        /// Имя метода например 'Home/Hello' без постфиксов 'Async'.
        /// </summary>
        public string ActionName { get; }
        public bool IsRequest => true;
        public bool IsNotificationRequest => Notification;

        /// <summary>
        /// ctor.
        /// </summary>
        /// <param name="interfaceMethod"></param>
        /// <param name="controllerName"></param>
        public RequestToSend(MethodInfo interfaceMethod, string controllerName)
        {
            Method = interfaceMethod;

            Notification = Attribute.IsDefined(interfaceMethod, typeof(NotificationAttribute));

            if(Notification)
            {
                if(Method.ReturnType != typeof(void) && Method.ReturnType != typeof(Task) && Method.ReturnType != typeof(ValueTask))
                {
                    throw new InvalidOperationException($"Метод '{interfaceMethod.Name}' помечен атрибутом [Notification] поэтому " +
                        $"возвращаемый тип метода может быть только void или Task или ValueTask.");
                }
            }
            IncapsulatedReturnType = GetMethodReturnType(interfaceMethod);
            ActionName = $"{controllerName}/{interfaceMethod.GetNameTrimAsync()}";
            IsAsync = interfaceMethod.IsAsyncMethod();
        }

        /// <summary>
        /// Возвращает инкапсулированный в <see cref="Task"/> тип результата функции.
        /// </summary>
        private static Type GetMethodReturnType(MethodInfo method)
        {
            // Если возвращаемый тип функции — Task.
            if (method.IsAsyncMethod())
            {
                // Если у задачи есть результат.
                if (method.ReturnType.IsGenericType)
                {
                    // Тип результата задачи.
                    Type resultType = method.ReturnType.GenericTypeArguments[0];
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
                return method.ReturnType;
            }
        }
    }
}
