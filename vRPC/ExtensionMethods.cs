using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    internal static class ExtensionMethods
    {
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

        //internal static void SerializeObjectProtobuf<T>(Stream destination, T instance)
        //{
        //    try
        //    {
        //        ProtoBuf.Serializer.Serialize(destination, instance);
        //        destination.Position = 0;
        //        var obj = ProtoBuf.Serializer.Deserialize<T>(destination);
        //    }
        //    catch (Exception ex)
        //    {

        //        throw;
        //    }
            
        //}

        /// <summary>
        /// Десериализует JSON.
        /// </summary>
        public static RequestMessage DeserializeRequestJson(Stream stream)
        {
            using (var reader = new StreamReader(stream, _UTF8NoBOM, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
            using (var json = new JsonTextReader(reader))
            {
                var ser = new JsonSerializer();
                var req = ser.Deserialize<RequestMessage>(json);
                if (req != null)
                    return req;

                // Сюда не должны попадать.
                throw new InvalidOperationException("Результатом десериализации оказался Null.");
            }
        }

        /// <summary>
        /// Десериализует JSON.
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
        public static string TrimAsyncPostfix(this MethodInfo method)
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

        [DebuggerStepThrough]
        public static SocketException ToException(this SocketError socketError)
        {
            return new SocketException((int)socketError);
        }
    }
}
