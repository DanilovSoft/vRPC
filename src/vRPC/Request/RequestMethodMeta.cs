using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
    [DebuggerDisplay(@"\{Request = {FullName}\}")]
    internal sealed class RequestMethodMeta /*: IMessageMeta*/
    {
        /// <summary>
        /// Инкапсулированный в Task тип результата функции. Может быть <see cref="VoidStruct"/> символизирующий тип void.
        /// </summary>
        public Type ReturnType { get; }
        /// <summary>
        /// True если метод интерфейса был помечен атрибутом <see cref="NotificationAttribute"/> 
        /// и соответственно не возвращает результат.
        /// </summary>
        public bool IsNotificationRequest { get; }
        /// <summary>
        /// Имя метода например 'Home/Hello' без постфикса 'Async'.
        /// </summary>
        public string FullName { get; }
        public bool TcpNoDelay { get; }
        public bool IsJsonRpc { get; }
        //public bool IsRequest => true;
        ///// <summary>
        ///// Когда все параметры метода являются <see cref="VRpcContent"/> то обрабатываются
        ///// по отдельному сценарию.
        ///// </summary>
        //private readonly bool _multipartStrategy;

        // ctor.
        /// <exception cref="VRpcException"/>
        public RequestMethodMeta(MethodInfo interfaceMethod, Type methodReturnType, string? controllerName)
        {
            Debug.Assert(interfaceMethod != null);

            ReturnType = methodReturnType;

            // Метод интерфейса может быть помечен как [Notification].
            IsNotificationRequest = Attribute.IsDefined(interfaceMethod, typeof(NotificationAttribute));
            TcpNoDelay = Attribute.IsDefined(interfaceMethod, typeof(TcpNoDelayAttribute));

            Debug.Assert(interfaceMethod.DeclaringType != null);
            IsJsonRpc = Attribute.IsDefined(interfaceMethod.DeclaringType, typeof(JsonRpcAttribute)) 
                || Attribute.IsDefined(interfaceMethod, typeof(JsonRpcAttribute));

            if (IsNotificationRequest)
            {
                // Метод интерфейса не должен возвращать значения.
                ValidateNotification(interfaceMethod.Name);
            }

            // Возвращаемый тип без учёта обвёртки Task<>.
            //IncapsulatedReturnType = GetMethodReturnType(interfaceMethod.ReturnType);
            
            // Нормализованное имя метода.
            FullName = $"{controllerName}{GlobalVars.ControllerNameSplitter}{interfaceMethod.GetNameTrimAsync()}";

            // Особая семантика метода — когда все параметры являются VRpcContent.
            //_multipartStrategy = IsAllParametersIsSpecialType(interfaceMethod);
        }

        /// <exception cref="VRpcException"/>
        private static bool IsAllParametersIsSpecialType(MethodInfo interfaceMethod)
        {
            ParameterInfo[]? prms = interfaceMethod.GetParameters();
            Debug.Assert(prms != null);
            if (prms.Length > 0)
            {
                bool allIsContentType = prms.All(x => typeof(VRpcContent).IsAssignableFrom(x.ParameterType));
                if (!allIsContentType)
                {
                    if (prms.Any(x => typeof(VRpcContent).IsAssignableFrom(x.ParameterType)))
                    {
                        ThrowHelper.ThrowVRpcException($"Все параметры должны быть либо производными типа {nameof(VRpcContent)} либо любыми другими типами");
                    }
                }
                return allIsContentType;
            }
            else
            {
                return false;
            }
        }

        // Используется для Internal вызовов таких как SignIn, SignOut.
        /// <exception cref="VRpcException"/>
        public RequestMethodMeta(string controllerName, string methodName, Type returnType, bool isNotification)
        {
            ReturnType = returnType;

            if (isNotification)
            {
                IsNotificationRequest = true;
                ValidateNotification(methodName);
            }
            else
            {
                IsNotificationRequest = false;
            }
            FullName = $"{controllerName}{GlobalVars.ControllerNameSplitter}{methodName}";
        }

        /// <exception cref="VRpcException"/>
        private void ValidateNotification(string methodName)
        {
            Debug.Assert(IsNotificationRequest);

            if (ReturnType != typeof(VoidStruct)
                && ReturnType != typeof(Task)
                && ReturnType != typeof(ValueTask))
            {
                ThrowHelper.ThrowVRpcException($"Метод '{methodName}' помечен атрибутом [Notification] поэтому " +
                    $"возвращаемый тип метода может быть только void или Task или ValueTask.");
            }
        }

        ///// <summary>
        ///// Возвращает инкапсулированный в <see cref="Task"/> тип результата функции.
        ///// </summary>
        //private static Type GetMethodReturnType(Type returnType)
        //{
        //    // Если возвращаемый тип функции — Task.
        //    if (returnType.IsAsyncReturnType())
        //    {
        //        // Если у задачи есть результат.
        //        if (returnType.IsGenericType)
        //        {
        //            // Тип результата задачи.
        //            Type resultType = returnType.GenericTypeArguments[0];
        //            return resultType;
        //        }
        //        else
        //        {
        //            // Возвращаемый тип Task(без результата).
        //            return typeof(void);
        //        }
        //    }
        //    else
        //    // Была вызвана синхронная функция.
        //    {
        //        return returnType;
        //    }
        //}

        internal object DeserializeVResponse(in HeaderDto header, ReadOnlyMemory<byte> payload, out VRpcException? vException)
        {
            Debug.Assert(header.IsRequest == false);

            if (header.StatusCode == StatusCode.Ok)
            // Запрос на удалённой стороне был выполнен успешно.
            {
                #region Передать успешный результат

                if (ReturnType != typeof(VoidStruct))
                // Поток ожидает некий объект как результат.
                {
                    if (!payload.IsEmpty)
                    {
                        try
                        {
                            vException = null;
                            return DeserializeResponse(ReturnType, payload, header.PayloadEncoding);
                        }
                        catch (Exception deserializationException)
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                            vException = new VRpcProtocolErrorException($"Ошибка десериализации ответа на запрос \"{FullName}\".", deserializationException);
                            return default!;
                        }
                    }
                    else
                    // У ответа отсутствует контент — это равнозначно Null.
                    {
                        if (ReturnType.CanBeNull())
                        // Результат запроса поддерживает Null.
                        {
                            vException = null;
                            return default!;
                        }
                        else
                        // Результатом этого запроса не может быть Null.
                        {
                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удалённой стороны.
                            vException = new VRpcProtocolErrorException($"Ожидался не пустой результат запроса но был получен ответ без результата.");
                            return default!;
                        }
                    }
                }
                else
                // void.
                {
                    vException = null;
                    return VoidStruct.RefInstance;
                }
                #endregion
            }
            else
            // Сервер прислал код ошибки.
            {
                // Телом ответа в этом случае будет строка.
                string errorMessage = payload.ReadAsString();

                // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                vException = ExceptionHelper.ToException(header.StatusCode, errorMessage);

                return default!;
            }
        }

        private static object DeserializeResponse(Type returnType, ReadOnlyMemory<byte> payload, string? contentEncoding)
        {
            return contentEncoding switch
            {
                KnownEncoding.ProtobufEncoding => ExtensionMethods.DeserializeProtoBuf(payload, returnType),
                _ => JsonSerializer.Deserialize(payload.Span, returnType), // Сериализатор по умолчанию.
            };
        }

        internal bool TrySerializeVRequest(object[] args, int? id,
            out int headerSize,
            [NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer,
            [NotNullWhen(false)] out VRpcSerializationException? exception)
        {
            buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                ExtensionMethods.SerializeRequestArgsJson(buffer, args);
                toDispose = null;
                exception = null;
            }
            catch (Exception ex)
            {
                exception = new VRpcSerializationException($"Не удалось сериализовать запрос в json.", ex);
                headerSize = -1;
                return false;
            }
            finally
            {
                toDispose?.Dispose();
            }

            var header = new HeaderDto(id, buffer.WrittenCount, contentEncoding: null, FullName);

            // Записать заголовок в конец стрима. Не бросает исключения.
            headerSize = header.SerializeJson(buffer);

            return true;
        }

        ///// <summary>
        ///// Сериализует сообщение в память. Может бросить исключение сериализации.
        ///// </summary>
        ///// <exception cref="Exception"/>
        //[Obsolete]
        //public SerializedMessageToSend SerializeRequest(object[] args)
        //{
        //    //if (!_multipartStrategy)
        //    {
        //        return SerializeToJson(args);
        //    }
        //    //else
        //    //{
        //    //    return SerializeToMultipart(args);
        //    //}
        //}

        //private SerializedMessageToSend SerializeToMultipart(object[] args)
        //{
        //    //Debug.Assert(_multipartStrategy);

        //    var serMsg = new SerializedMessageToSend(this)
        //    {
        //        ContentEncoding = KnownEncoding.MultipartEncoding,
        //        Parts = new Multipart[args.Length]
        //    };
        //    SerializedMessageToSend? toDispose = serMsg;
        //    try
        //    {
        //        for (int i = 0; i < args.Length; i++)
        //        {
        //            using (VRpcContent? part = args[i] as VRpcContent)
        //            {
        //                Debug.Assert(part != null, "Мы заранее проверяли что все аргументы являются VRpcContent.");

        //                if (part.TryComputeLength(out int length))
        //                {
        //                    if (serMsg.Buffer.FreeCapacity < length)
        //                    {
        //                        //serMsg.MemoryPoolBuffer.Advance(length);
        //                    }
        //                }
        //                serMsg.Parts[i] = part.InnerSerializeToStream(serMsg.Buffer);
        //            }
        //        }
        //        toDispose = null;
        //        return serMsg;
        //    }
        //    finally
        //    {
        //        toDispose?.Dispose();
        //    }
        //}

        //private SerializedMessageToSend SerializeToJson(object[] args)
        //{
        //    var serializedMessage = new SerializedMessageToSend(this);
        //    SerializedMessageToSend? toDispose = serializedMessage;
        //    try
        //    {
        //        ExtensionMethods.SerializeObjectJson(serializedMessage.Buffer, args);
        //        toDispose = null;
        //        return serializedMessage;
        //    }
        //    finally
        //    {
        //        toDispose?.Dispose();
        //    }
        //}
    }
}
