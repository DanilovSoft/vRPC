using DanilovSoft;
using DanilovSoft.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    internal static class ExtensionMethods
    {
        /// <summary>
        /// Используется Bson сериализатором по умолчанию.
        /// </summary>
        private static readonly Encoding _UTF8NoBOM;

        // ctor.
        static ExtensionMethods()
        {
            _UTF8NoBOM = new UTF8Encoding(false, true);
        }

        /// <summary>
        /// Сериализует объект в JSON.
        /// </summary>
        public static void SerializeObjectJson(Stream destination, object instance)
        {
            using (var writer = new StreamWriter(destination, _UTF8NoBOM, bufferSize: 1024, leaveOpen: true))
            using (var json = new JsonTextWriter(writer))
            {
                var ser = new JsonSerializer();
                ser.Serialize(json, instance);
            }
        }

        /// <summary>
        /// Сериализует объект в BSON.
        /// </summary>
        public static void SerializeObjectBson(Stream stream, object instance)
        {
            using (var bw = new BinaryWriter(stream, _UTF8NoBOM, leaveOpen: true))
            using (var json = new BsonDataWriter(bw)) // Использует new UTF8Encoding(false, true)
            {
                var ser = new JsonSerializer();
                ser.Serialize(json, instance);
            }
        }

        public static bool TryReadLengthPrefix(Stream source, out int length)
        {
            return ProtoBuf.Serializer.TryReadLengthPrefix(source, ProtoBuf.PrefixStyle.Base128, out length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static object DeserializeProtobuf(Stream source, Type type)
        {
            return ProtoBuf.Serializer.Deserialize(type, source);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SerializeObjectProtobuf(Stream destination, object instance)
        {
            ProtoBuf.Serializer.Serialize(destination, instance);
        }

        /// <summary>
        /// Десериализует Json.
        /// </summary>
        public static RequestMessageDto DeserializeRequestJson(Stream stream)
        {
            using (var reader = new StreamReader(stream, _UTF8NoBOM, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            using (var json = new JsonTextReader(reader))
            {
                var ser = new JsonSerializer();
                var req = ser.Deserialize<RequestMessageDto>(json);
                if (req != null)
                    return req;

                // Сюда не должны попадать.
                throw new InvalidOperationException("Результатом десериализации оказался Null.");
            }
        }

        /// <summary>
        /// Десериализует Bson.
        /// </summary>
        public static RequestMessageDto DeserializeRequestBson(Stream stream)
        {
            using(var br = new BinaryReader(stream, _UTF8NoBOM, leaveOpen: true))
            using (var json = new BsonDataReader(br))
            {
                var ser = new JsonSerializer();
                var req = ser.Deserialize<RequestMessageDto>(json);
                if (req != null)
                    return req;

                // Сюда не должны попадать.
                throw new InvalidOperationException("Результатом десериализации оказался Null.");
            }
        }

        public static void WarmupRequestMessageSerialization()
        {
            var dto = new RequestMessageDto
            {
                ActionName = "n",
                Args = new JToken[] { JToken.FromObject(1) }
            };

            using (var mem = new MemoryStream())
            {
                SerializeObjectBson(mem, dto);
                mem.Position = 0;
                using (var json = new BsonDataReader(mem))
                {
                    var ser = new JsonSerializer();
                    ser.Deserialize<RequestMessageDto>(json);
                }
            }
        }

        /// <summary>
        /// Десериализует Json.
        /// </summary>
        public static object DeserializeJson(Stream stream, Type objectType)
        {
            using (var reader = new StreamReader(stream, _UTF8NoBOM, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            using (var json = new JsonTextReader(reader))
            {
                var ser = new JsonSerializer();
                return ser.Deserialize(json, objectType);
            }
        }

        /// <summary>
        /// Читает строку в формате Utf-8.
        /// </summary>
        public static string ReadAsString(this Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                // Десериализовать тело как строку.
                string errorMessage = reader.ReadString();
                return errorMessage;
            }
        }

        /// <summary>
        /// Записывает строку в формате Utf-8.
        /// </summary>
        public static void WriteStringBinary(this Stream destination, string message)
        {
            using (var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true))
                writer.Write(message);
        }

        /// <summary>
        /// Возвращает <see langword="true"/> если функция имеет возвращаемый тип <see cref="Task"/> (<see cref="Task{TResult}"/>)
        /// или <see cref="ValueTask"/> (<see cref="ValueTask{TResult}"/>).
        /// </summary>
        public static bool IsAsyncMethod(this MethodInfo methodInfo)
        {
            if (
                typeof(Task).IsAssignableFrom(methodInfo.ReturnType) // Task и Task<T>
                || methodInfo.ReturnType == typeof(ValueTask)
                || (methodInfo.ReturnType.IsGenericType && methodInfo.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Нормализует имя функции если она является Task-Async.
        /// </summary>
        public static string GetNameTrimAsync(this MethodInfo method)
        {
            if (method.IsAsyncMethod())
            // Это асинхронный метод.
            {
                // Убрать слово Async.
                int index = method.Name.LastIndexOf("Async", StringComparison.Ordinal);
                if (index != -1)
                {
                    return method.Name.Remove(index);
                }
            }
            return method.Name;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="IOException"/>
        /// <exception cref="SocketException"/>
        //[DebuggerStepThrough]
        public static Exception ToException(this ReceiveResult receiveResult)
        {
            if (receiveResult.SocketError != SocketError.Success)
            {
                return new SocketException((int)receiveResult.SocketError);
            }
            else
            {
                return new IOException("Unexpected connection closed during read.");
            }
        }

        //[DebuggerStepThrough]
        public static SocketException ToException(this SocketError socketError)
        {
            return new SocketException((int)socketError);
        }

        // TODO закэшировать?
        /// <summary>
        /// Возвращает инкапсулированный в <see cref="Task"/> тип результата функции.
        /// </summary>
        public static Type GetMethodReturnType(this MethodInfo method)
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

        public static Exception ToException(this CloseReason closeReason)
        {
            if(closeReason.Error == null)
            // Закрытие было грациозное но нам всёравно нужно исключение.
            {
                if(closeReason.CloseStatus == WebSocketCloseStatus.NormalClosure)
                {
                    return new ConnectionClosedException(closeReason.CloseDescription);
                }
                else
                {
                    return new ConnectionClosedException(GetMessageFromCloseFrame(closeReason.CloseStatus, closeReason.CloseDescription));
                }
            }
            else
            {
                return closeReason.Error;
            }
        }

        /// <summary>
        /// Формирует сообщение ошибки из фрейма веб-сокета информирующем о закрытии соединения.
        /// </summary>
        private static string GetMessageFromCloseFrame(WebSocketCloseStatus? closeStatus, string closeDescription)
        {
            //var webSocket = _socket.WebSocket;

            string exceptionMessage = null;
            if (closeStatus != null)
            {
                exceptionMessage = $"CloseStatus: {closeStatus}";

                if (!string.IsNullOrEmpty(closeDescription))
                {
                    exceptionMessage += $", Description: \"{closeDescription}\"";
                }
            }
            else if (!string.IsNullOrEmpty(closeDescription))
            {
                exceptionMessage = $"Description: \"{closeDescription}\"";
            }

            if (exceptionMessage == null)
                exceptionMessage = "Удалённая сторона закрыла соединение без объяснения причины.";

            return exceptionMessage;
        }
    }
}
