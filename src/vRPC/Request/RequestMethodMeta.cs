using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Описывает метод который требуется вызвать на удалённой стороне.
    /// Не подлежит сериализации.
    /// Потокобезопасен.
    /// </summary>
    [DebuggerDisplay(@"\{Request = {ActionFullName}\}")]
    internal sealed class RequestMethodMeta : IMessageMeta
    {
        //public Type ReturnType { get; }
        /// <summary>
        /// Инкапсулированный в Task тип результата функции.
        /// </summary>
        public Type IncapsulatedReturnType { get; }
        /// <summary>
        /// True если метод интерфейса был помечен атрибутом <see cref="NotificationAttribute"/> 
        /// и соответственно не возвращает результат.
        /// </summary>
        public bool IsNotificationRequest { get; }
        ///// <summary>
        ///// Возвращает <see langword="true"/> если функция имеет возвращаемый тип <see cref="Task"/> (<see cref="Task{TResult}"/>)
        ///// или <see cref="ValueTask"/> (<see cref="ValueTask{TResult}"/>).
        ///// </summary>
        //public bool IsAsync { get; }
        /// <summary>
        /// Имя метода например 'Home/Hello' без постфикса 'Async'.
        /// </summary>
        public string ActionFullName { get; }
        public bool IsRequest => true;
        /// <summary>
        /// Когда все параметры метода являются <see cref="VRpcContent"/> то обрабатываются
        /// по отдельному сценарию.
        /// </summary>
        private readonly bool _multipartStrategy;

        // ctor.
        /// <exception cref="VRpcException"/>
        public RequestMethodMeta(MethodInfo interfaceMethod, string? controllerName)
        {
            Debug.Assert(interfaceMethod != null);

            //ReturnType = interfaceMethod.ReturnType;

            // Метод интерфейса может быть помечен как [Notification].
            IsNotificationRequest = Attribute.IsDefined(interfaceMethod, typeof(NotificationAttribute));

            if (IsNotificationRequest)
            {
                // Метод интерфейса не должен возвращать значения.
                ValidateNotification(interfaceMethod.ReturnType, interfaceMethod.Name);
            }

            // Возвращаемый тип без учёта обвёртки Task<>.
            IncapsulatedReturnType = GetMethodReturnType(interfaceMethod.ReturnType);
            
            // Нормализованное имя метода.
            ActionFullName = $"{controllerName}{GlobalVars.ControllerNameSplitter}{interfaceMethod.GetNameTrimAsync()}";
            
            // Метод считается асинхронным есть возвращаемый тип Task или ValueTask.
            //IsAsync = interfaceMethod.ReturnType.IsAsyncReturnType();

            // Особая семантика метода — когда все параметры являются VRpcContent.
            _multipartStrategy = IsAllParametersIsSpecialType(interfaceMethod);
        }

        /// <exception cref="VRpcException"/>
        private static bool IsAllParametersIsSpecialType(MethodInfo interfaceMethod)
        {
            ParameterInfo[]? prms = interfaceMethod.GetParameters();
            bool allIsContentType = prms.All(x => typeof(VRpcContent).IsAssignableFrom(x.ParameterType));
            if (!allIsContentType)
            {
                if (prms.Any(x => typeof(VRpcContent).IsAssignableFrom(x.ParameterType)))
                {
                    throw new VRpcException($"Все параметры должны быть либо производными типа {nameof(VRpcContent)} либо любыми другими типами");
                }
            }
            return allIsContentType;
        }

        // Используется для Internal вызовов таких как SignIn, SignOut.
        /// <exception cref="VRpcException"/>
        public RequestMethodMeta(string controllerName, string methodName, Type returnType, bool notification)
        {
            //ReturnType = returnType;
            IsNotificationRequest = notification;

            if (notification)
            {
                ValidateNotification(returnType, methodName);
            }

            IncapsulatedReturnType = GetMethodReturnType(returnType);
            ActionFullName = $"{controllerName}{GlobalVars.ControllerNameSplitter}{methodName}";
            //IsAsync = returnType.IsAsyncReturnType();
        }

        /// <exception cref="VRpcException"/>
        private static void ValidateNotification(Type returnType, string methodName)
        {
            if (returnType != typeof(void) && returnType != typeof(Task) && returnType != typeof(ValueTask))
            {
                throw new VRpcException($"Метод '{methodName}' помечен атрибутом [Notification] поэтому " +
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
        /// <exception cref="Exception"/>
        public SerializedMessageToSend SerializeRequest(object[] args)
        {
            if (!_multipartStrategy)
            {
                return SerializeToJson(args);
            }
            else
            {
                return SerializeToMultipart(args);
            }
        }

        private SerializedMessageToSend SerializeToMultipart(object[] args)
        {
            Debug.Assert(_multipartStrategy);

            var serMsg = new SerializedMessageToSend(this)
            {
                ContentEncoding = KnownEncoding.MultipartEncoding,
                Parts = new Multipart[args.Length]
            };
            SerializedMessageToSend? toDispose = serMsg;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    using (VRpcContent? part = args[i] as VRpcContent)
                    {
                        Debug.Assert(part != null, "Мы заранее проверяли что все аргументы являются VRpcContent.");

                        if (part.TryComputeLength(out int length))
                        {
                            var requiredSize = (int)serMsg.MemPoolStream.Length + length;
                            if (serMsg.MemPoolStream.Capacity < requiredSize)
                                serMsg.MemPoolStream.Capacity = requiredSize;
                        }
                        serMsg.Parts[i] = part.InnerSerializeToStream(serMsg.MemPoolStream);
                    }
                }
                toDispose = null;
                return serMsg;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        private SerializedMessageToSend SerializeToJson(object[] args)
        {
            SerializedMessageToSend serializedMessage = new SerializedMessageToSend(this);
            SerializedMessageToSend? toDispose = serializedMessage;
            try
            {
                ExtensionMethods.SerializeObjectJson(serializedMessage.MemPoolStream, args);
                toDispose = null;
                return serializedMessage;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }
    }
}
