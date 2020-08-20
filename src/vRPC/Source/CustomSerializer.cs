using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using ProtoBuf;
using DanilovSoft.vRPC.DTO;
using DanilovSoft.vRPC.Source;
using System.Buffers;
using System.Linq;
using System.Buffers.Text;

namespace DanilovSoft.vRPC
{
    internal static partial class CustomSerializer
    {
        /// <param name="result">Не Null когда True.</param>
        /// <remarks>Не бросает исключения.</remarks>
        internal static bool TryDeserializeRequest(ReadOnlyMemory<byte> content, ControllerMethodMeta method, in HeaderDto header, 
            [MaybeNullWhen(false)] out RequestContext result,
            [MaybeNullWhen(true)] out IActionResult? error)
        {
            try
            {
                if (header.PayloadEncoding != KnownEncoding.MultipartEncoding)
                {
                    return TryDeserializeRequestJson(content.Span, method, header.Id, out result, out error);
                }
                else
                {
                    return TryDeserializeMultipart(content, method, header.Id, out result, out error);
                }
            }
            catch (Exception ex)
            // Ошибка десериализации запроса.
            {
                // Игнорируем этот запрос и отправляем обратно ошибку.
                if (header.IsResponseRequired)
                // Запрос ожидает ответ.
                {
                    // Подготовить ответ с ошибкой.
                    error = new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{ex.Message}\".");
                    result = default;
                    return false;
                }
                else
                {
                    error = null;
                    result = default;
                    return false;
                }
            }
        }

        /// <summary>
        /// Десериализует json запрос.
        /// </summary>
        /// <exception cref="JsonException"/>
        /// <returns>True если успешно десериализовали.</returns>
        private static bool TryDeserializeRequestJson(ReadOnlySpan<byte> utf8Json, ControllerMethodMeta method, int? uid, 
            [MaybeNullWhen(false)] out RequestContext result,
            [MaybeNullWhen(true)] out IActionResult? error)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            object[] args = method.Parametergs.Length == 0
                ? Array.Empty<object>()
                : (new object[method.Parametergs.Length]);

            // Считаем сколько аргументов есть в json'е.
            short argsInJsonCounter = 0;
            
            var reader = new Utf8JsonReader(utf8Json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (method.Parametergs.Length > argsInJsonCounter)
                        {
                            Type paramType = method.Parametergs[argsInJsonCounter].ParameterType;
                            try
                            {
                                args[argsInJsonCounter] = JsonSerializer.Deserialize(ref reader, paramType);
                            }
                            catch (JsonException)
                            {
                                result = default;
                                error = ResponseHelper.ErrorDeserializingArgument(method.MethodFullName, argIndex: argsInJsonCounter, paramType);
                                return false;
                            }
                            argsInJsonCounter++;
                        }
                        else
                        // Несоответствие числа параметров.
                        {
                            result = default;
                            error = ResponseHelper.ArgumentsCountMismatchError(method.MethodFullName, method.Parametergs.Length);
                            return false;
                        }
                    }
                }
            }

            if (ResponseHelper.ValidateArgumentsCount(method.Parametergs, argsInJsonCounter, method.MethodFullName, out error))
            {
                error = null;
                result = new RequestContext(uid, method, args);
                return true;
            }
            else
            // Не соответствует число аргументов.
            {
                result = default;
                return false;
            }
        }

        /// <exception cref="Exception"/>
        private static bool TryDeserializeMultipart(ReadOnlyMemory<byte> content, ControllerMethodMeta method, int? uid,
            [MaybeNullWhen(false)] out RequestContext result,
            [MaybeNullWhen(true)] out IActionResult? error)
        {
            object[]? args;
            if (method.Parametergs.Length == 0)
            {
                args = Array.Empty<object>();
            }
            else
            {
                args = new object[method.Parametergs.Length];
            }
            try
            {
                if (DeserializeProtoBufArgs(content, method, args, out error))
                {
                    result = new RequestContext(uid, method, args);
                    args = null; // Предотвратить Dispose.
                    error = null;
                    return true;
                }
                else
                {
                    result = default;
                    return false;
                }
            }
            finally
            {
                if (args != null)
                {
                    method.DisposeArgs(args);
                }
            }
        }

        private static bool DeserializeProtoBufArgs(ReadOnlyMemory<byte> content, ControllerMethodMeta method, object[] args, [MaybeNullWhen(true)] out IActionResult? error)
        {
            using (var stream = new ReadOnlyMemoryStream(content))
            {
                for (short i = 0; i < method.Parametergs.Length; i++)
                {
                    Type argType = method.Parametergs[i].ParameterType;

                    var partHeader = Serializer.DeserializeWithLengthPrefix<MultipartHeaderDto>(stream, PrefixStyle.Base128, 1);

                    if (partHeader.Encoding == KnownEncoding.ProtobufEncoding)
                    {
                        using (var argStream = new ReadOnlyMemoryStream(content.Slice((int)stream.Position, partHeader.Size)))
                        {
                            try
                            {
                                args[i] = Serializer.NonGeneric.Deserialize(argType, argStream);
                            }
                            catch (Exception)
                            {
                                error = ResponseHelper.ErrorDeserializingArgument(method.MethodFullName, argIndex: i, argType);
                                return false;
                            }
                        }
                    }
                    else if (partHeader.Encoding == KnownEncoding.RawEncoding)
                    {
                        ReadOnlyMemory<byte> raw = content.Slice((int)stream.Position, partHeader.Size);

                        if (!RawEncodingArg(raw, ref args[i], argType))
                        {
                            error = ResponseHelper.ErrorDeserializingArgument(method.MethodFullName, argIndex: i, argType);
                            return false;
                        }
                    }
                    stream.Position += partHeader.Size;
                }
            }
            error = null;
            return true;
        }

        private static bool RawEncodingArg(ReadOnlyMemory<byte> raw, ref object argv, Type argType)
        {
            if (argType == typeof(RentedMemory))
            {
                IMemoryOwner<byte>? shared = MemoryPool<byte>.Shared.Rent(raw.Length);
                raw.CopyTo(shared.Memory);
                var disposableMemory = new RentedMemory(shared, raw.Length);
                argv = disposableMemory;
            }
            else if (argType == typeof(byte[]))
            {
                var array = new byte[raw.Length];
                raw.CopyTo(array);
                argv = array;
            }
            else
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Десериализует json запрос.
        /// </summary>
        /// <exception cref="JsonException"/>
        /// <returns>True если успешно десериализовали.</returns>
        internal static HeaderDto DeserializeHeader(ReadOnlySpan<byte> utf8Json)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            StatusCode statusCode = StatusCode.None;
            int? id = null;
            int payloadLength = -1;
            string? actionName = null;
            string? contentEncoding = null;

            bool gotCode = false;
            bool gotId = false;
            bool gotPayload = false;
            bool gotEncoding = false;
            bool gotMethod = false;

            var reader = new Utf8JsonReader(utf8Json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (!gotCode && reader.ValueTextEquals("code"))
                    {
                        if (reader.Read())
                        {
                            statusCode = (StatusCode)reader.GetInt32();
                            gotCode = true;
                        }
                    }
                    else if (!gotId && reader.ValueTextEquals("uid"))
                    {
                        if (reader.Read())
                        {
                            id = reader.GetInt32();
                            gotId = true;
                        }
                    }
                    else if (!gotPayload && reader.ValueTextEquals("payload"))
                    {
                        if (reader.Read())
                        {
                            payloadLength = reader.GetInt32();
                            gotPayload = true;
                        }
                    }
                    else if (!gotEncoding && reader.ValueTextEquals("encoding"))
                    {
                        if (reader.Read())
                        {
                            contentEncoding = reader.GetString();
                            gotEncoding = true;
                        }
                    }
                    else if (!gotMethod && reader.ValueTextEquals("method"))
                    {
                        if (reader.Read())
                        {
                            actionName = reader.GetString();
                            gotMethod = true;
                        }
                    }
                }
            }
            return new HeaderDto(id, statusCode, payloadLength, contentEncoding, actionName);
        }
    }
}
