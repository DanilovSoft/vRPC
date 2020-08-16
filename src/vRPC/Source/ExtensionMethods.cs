using DanilovSoft;
using DanilovSoft.WebSockets;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal static class ExtensionMethods
    {
        /// <summary>
        /// Сериализует объект в JSON.
        /// </summary>
        /// <exception cref="VRpcException"/>
        public static void SerializeObjectJson(ArrayBufferWriter<byte> destination, object instance)
        {
            // Сериализовать Null не нужно (Отправлять тело сообщения при этом тоже не нужно).
            Debug.Assert(instance != null, "Сериализовать и отправлять Null не нужно");

            try
            {
                using (var writer = new Utf8JsonWriter(destination))
                {
                    JsonSerializer.Serialize(writer, instance/*, new JsonSerializerOptions { IgnoreNullValues = true }*/);
                }
            }
            catch (Exception ex)
            {
                ThrowHelper.ThrowVRpcException($"Не удалось сериализовать объект типа {instance.GetType().FullName} в json.", ex);
            }
        }

        /// <summary>
        /// Сериализует объект в JSON.
        /// </summary>
        public static void SerializeObjectJson<T>(Stream destination, T instance)
        {
            using (var writer = new Utf8JsonWriter(destination))
            {
                JsonSerializer.Serialize(writer, instance);
            }
        }

        /// <summary>
        /// Десериализует Json.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object DeserializeJson(ReadOnlySpan<byte> utf8Json, Type returnType)
        {
            return System.Text.Json.JsonSerializer.Deserialize(utf8Json, returnType);
        }

        /// <summary>
        /// Десериализует Json.
        /// </summary>
        //[Obsolete]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object DeserializeJson(ReadOnlyMemory<byte> utf8Json, Type returnType)
        {
            return JsonSerializer.Deserialize(utf8Json.Span, returnType);
        }

        /// <summary>
        /// Десериализует Json.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T DeserializeJson<T>(ReadOnlySpan<byte> utf8Json)
        {
            return JsonSerializer.Deserialize<T>(utf8Json);
        }

        public static bool TryReadLengthPrefix(Stream source, out int length)
        {
            return ProtoBuf.Serializer.TryReadLengthPrefix(source, ProtoBuf.PrefixStyle.Base128, out length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static object DeserializeProtoBuf(Stream source, Type type)
        {
            return ProtoBuf.Serializer.Deserialize(type, source);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T DeserializeProtoBuf<T>(ReadOnlyMemory<byte> source)
        {
            using (var stream = new MemoryReader(source))
            {
                return ProtoBuf.Serializer.Deserialize<T>(stream);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SerializeObjectProtobuf(ArrayBufferWriter<byte> destination, object instance)
        {
            using (var mem = new ReadOnlyMemoryStream(destination.WrittenMemory))
            {
                ProtoBuf.Serializer.Serialize(mem, instance);
            }
        }

        public static void WarmupRequestMessageJson()
        {
            //var dto = new RequestMessageDto("Home/Hello", new object[] { 1 });

            using (var mem = new MemoryStream())
            {
                SerializeObjectJson(mem, Array.Empty<object>());
                //mem.Position = 0;
                //DeserializeJson<RequestMessageDto>(mem.GetBuffer().AsSpan(0, (int)mem.Length));
                //using (var reader = new StreamReader(mem))
                //using (var json = new JsonTextReader(reader))
                //{
                //    var ser = new JsonSerializer();
                //    ser.Deserialize<RequestMessageDto>(json);
                //}
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

#if NETSTANDARD2_0 || NET472

        /// <summary>
        /// Читает строку в формате Utf-8.
        /// </summary>
        public static string ReadAsString(this ReadOnlyMemory<byte> readOnlyMemory)
        {
            using (var stream = new MemoryReader(readOnlyMemory))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                // Десериализовать тело как строку.
                string errorMessage = reader.ReadString();
                return errorMessage;
            }
        }

        /// <summary>
        /// Читает строку в формате Utf-8.
        /// </summary>
        public static string ReadAsString(this Memory<byte> memory)
        {
            return ReadAsString(readOnlyMemory: memory);
        }

#else
        /// <summary>
        /// Читает строку в формате Utf-8.
        /// </summary>
        public static string ReadAsString(this Memory<byte> memory)
        {
            return ReadAsString(readOnlyMemory: memory);
        }

        /// <summary>
        /// Читает строку в формате Utf-8.
        /// </summary>
        public static string ReadAsString(this ReadOnlyMemory<byte> readOnlyMemory)
        {
            string errorMessage = Encoding.UTF8.GetString(readOnlyMemory.Span);
            return errorMessage;
        }
#endif

#if NETSTANDARD2_0 || NET472
        /// <summary>
        /// Записывает строку в формате Utf-8.
        /// </summary>
        public static void WriteStringBinary(this IBufferWriter<byte> destination, string message)
        {
            int bytesCount = Encoding.UTF8.GetByteCount(message);
            Span<byte> span = destination.GetSpan(bytesCount);

            byte[] data = ArrayPool<byte>.Shared.Rent(bytesCount);
            try
            {
                int bytesWriten = Encoding.UTF8.GetBytes(message, 0, message.Length, data, 0);
                data.AsSpan(0, bytesWriten).CopyTo(span);
                destination.Advance(bytesWriten);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data);
            }
        }
#else
        /// <summary>
        /// Записывает строку в формате Utf-8.
        /// </summary>
        public static void WriteStringBinary(this IBufferWriter<byte> destination, string message)
        {
            int bytesCount = Encoding.UTF8.GetByteCount(message);
            Span<byte> span = destination.GetSpan(bytesCount);
            int bytesWriten = Encoding.UTF8.GetBytes(message, span);

            // Сообщаем райтеру сколько полезных данных мы записали в его память.
            destination.Advance(bytesWriten);
        }
#endif

        /// <summary>
        /// Возвращает <see langword="true"/> если функция имеет возвращаемый тип <see cref="Task"/> или <see cref="Task{TResult}"/>
        /// или <see cref="ValueTask"/> или <see cref="ValueTask{TResult}"/>.
        /// </summary>
        public static bool IsAsyncMethod(this MethodInfo methodInfo)
        {
            return IsAsyncReturnType(methodInfo.ReturnType);
        }

        /// <summary>
        /// Возвращает <see langword="true"/> если тип является <see cref="Task"/> или <see cref="Task{T}"/>
        /// или <see cref="ValueTask"/> или <see cref="ValueTask{TResult}"/>.
        /// </summary>
        public static bool IsAsyncReturnType(this Type returnType)
        {
            // Task, Task<T> и ValueTask, ValueTask<T>
            bool isTask = typeof(Task).IsAssignableFrom(returnType)
                || returnType == typeof(ValueTask)
                || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

            return isTask;
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

        public static Exception ToException(this CloseReason closeReason)
        {
            if(closeReason.ConnectionError == null)
            // Закрытие было грациозное но нам всёравно нужно исключение.
            {
                if(closeReason.CloseStatus == WebSocketCloseStatus.NormalClosure)
                {
                    return new VRpcConnectionClosedException(closeReason.CloseDescription ?? VRpcConnectionClosedException.ConnectionClosedNormallyMessage);
                }
                else
                {
                    return new VRpcConnectionClosedException(GetMessageFromCloseFrame(closeReason.CloseStatus, closeReason.CloseDescription));
                }
            }
            else
            {
                return closeReason.ConnectionError;
            }
        }

        /// <summary>
        /// Формирует сообщение ошибки из фрейма веб-сокета информирующем о закрытии соединения. Пустую строку не возвращает.
        /// </summary>
        private static string GetMessageFromCloseFrame(WebSocketCloseStatus? closeStatus, string? closeDescription)
        {
            string? exceptionMessage = null;
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
                exceptionMessage = VRpcConnectionClosedException.ConnectionClosedNormallyMessage;

            return exceptionMessage;
        }

        internal static string GetControllerActionName(this MethodInfo controllerMethod)
        {
            Debug.Assert(controllerMethod != null);
            Debug.Assert(controllerMethod.DeclaringType != null);

            string controllerName = controllerMethod.DeclaringType.Name.TrimEnd("Controller");
            return $"{controllerName}/{controllerMethod.Name}";
        }

        public static string TrimEnd(this string s, string value)
        {
            int index = s.LastIndexOf(value, StringComparison.Ordinal);
            if(index != -1)
            {
                return s.Remove(index);
            }
            return s;
        }

        public static bool CanBeNull(this Type type)
        {
            bool canBeNull = !type.IsValueType || (Nullable.GetUnderlyingType(type) != null);
            return canBeNull;
        }

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCompletedSuccessfully(this Task t)
        {
#if NETSTANDARD2_0 || NET472

            return t.Status == TaskStatus.RanToCompletion;
#else
            return t.IsCompletedSuccessfully;
#endif
        }

        /// <summary>
        /// Не бросает исключения.
        /// </summary>
        internal static ConnectResult ToPublicConnectResult(this in InnerConnectionResult conRes)
        {
            if (conRes.Connection != null)
            {
                return ConnectResult.FromConnectionSuccess();
            }
            else if (conRes.SocketError != null)
            {
                return ConnectResult.FromError(conRes.SocketError.Value);
            }
            else
            {
                Debug.Assert(conRes.ShutdownRequest != null);
                return ConnectResult.FromShutdownRequest(conRes.ShutdownRequest);
            }
        }

        internal static void ValidateAccessToken(this AccessToken accessToken, string arguemntName)
        {
            if (accessToken.Bytes.Length == 0)
                ThrowHelper.ThrowArgumentOutOfRangeException("AccessToken is empty", arguemntName);
        }

        internal static VRpcShutdownException ToException(this ShutdownRequest shutdownRequest)
        {
            return new VRpcShutdownException(shutdownRequest);
        }
    }
}

namespace System
{
    internal static class ExtensionMethods
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsClrType(this Type type)
        {
            return type.IsPrimitive
                || type.Assembly == typeof(int).Assembly; // CoreLib.
        }
    }
}
