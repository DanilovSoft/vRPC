using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class InterfaceMethodInfo
    {
        public MethodInfo Method { get; }
        /// <summary>
        /// Инкапсулированный в Task тип результата функции.
        /// </summary>
        public Type IncapsulatedReturnType { get; }
        public bool Notification { get; }
        public bool IsAsync { get; }
        /// <summary>
        /// Имя метода без постфикса Async.
        /// </summary>
        public string MethodName { get; }

        public InterfaceMethodInfo(MethodInfo methodInfo)
        {
            Method = methodInfo;

            Notification = Attribute.IsDefined(methodInfo, typeof(NotificationAttribute));

            if(Notification)
            {
                if(Method.ReturnType != typeof(void) && Method.ReturnType != typeof(Task) && Method.ReturnType != typeof(ValueTask))
                {
                    throw new InvalidOperationException($"Метод '{methodInfo.Name}' помечен атрибутом [Notification] поэтому " +
                        $"возвращаемый тип метода может быть только void или Task или ValueTask.");
                }
            }
            IncapsulatedReturnType = GetMethodReturnType(methodInfo);
            MethodName = methodInfo.GetNameTrimAsync();
            IsAsync = methodInfo.IsAsyncMethod();
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
