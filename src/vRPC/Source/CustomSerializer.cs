using System;
using System.Collections.Generic;
using System.Text;
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
    internal static class CustomSerializer
    {
        private const string ArgumentsCountMismatch = "Argument count mismatch for action '{0}'. {1} arguments was expected.";

        /// <param name="result">Не Null когда True.</param>
        /// <remarks>Не бросает исключения.</remarks>
        internal static bool TryDeserializeRequest(ReadOnlyMemory<byte> content, ControllerActionMeta action, in HeaderDto header, 
            [MaybeNullWhen(false)] out RequestContext result,
            [MaybeNullWhen(true)] out IActionResult? error)
        {
            try
            {
                if (header.PayloadEncoding != KnownEncoding.MultipartEncoding)
                {
                    return TryDeserializeRequestJson(content.Span, action, header.Uid, out result, out error);
                }
                else
                {
                    return TryDeserializeMultipart(content, action, header.Uid, out result, out error);
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
        private static bool TryDeserializeRequestJson(ReadOnlySpan<byte> utf8Json, ControllerActionMeta action, int? uid, 
            [MaybeNullWhen(false)] out RequestContext result,
            [MaybeNullWhen(true)] out IActionResult? error)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            object[] args = action.Parametergs.Length == 0
                ? Array.Empty<object>()
                : (new object[action.Parametergs.Length]);

            // Считаем сколько аргументов есть в json'е.
            short argsInJsonCounter = 0;
            
            var reader = new Utf8JsonReader(utf8Json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (action.Parametergs.Length > argsInJsonCounter)
                        {
                            Type paramType = action.Parametergs[argsInJsonCounter].ParameterType;
                            try
                            {
                                args[argsInJsonCounter] = JsonSerializer.Deserialize(ref reader, paramType);
                            }
                            catch (JsonException)
                            {
                                result = default;
                                error = ErrorDeserializingArgument(action.ActionFullName, argIndex: argsInJsonCounter, paramType);
                                return false;
                            }
                            argsInJsonCounter++;
                        }
                        else
                        // Выход за границы массива.
                        {
                            result = default;
                            error = ArgumentsCountMismatchError(action.ActionFullName, action.Parametergs.Length);
                            return false;
                        }
                    }
                }
            }

            if (ValidateArgumentsCount(action.Parametergs, argsInJsonCounter, action.ActionFullName, out error))
            {
                error = null;
                result = new RequestContext(uid, action, args);
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
        private static bool TryDeserializeMultipart(ReadOnlyMemory<byte> content, ControllerActionMeta action, int? uid,
            [MaybeNullWhen(false)] out RequestContext result,
            [MaybeNullWhen(true)] out IActionResult? error)
        {
            object[]? args;
            if (action.Parametergs.Length == 0)
            {
                args = Array.Empty<object>();
            }
            else
            {
                args = new object[action.Parametergs.Length];
            }
            try
            {
                if (DeserializeMultipartArgs(content, action, args, out error))
                {
                    result = new RequestContext(uid, action, args);
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
                    action.DisposeArgs(args);
                }
            }
        }

        private static bool DeserializeMultipartArgs(ReadOnlyMemory<byte> content, ControllerActionMeta action, object[] args, [MaybeNullWhen(true)] out IActionResult? error)
        {
            throw new NotImplementedException();
            using (var stream = new ReadOnlyMemoryStream(content))
            {
                for (short i = 0; i < action.Parametergs.Length; i++)
                {
                    Type argType = action.Parametergs[i].ParameterType;

                    //var partHeader = Serializer.DeserializeWithLengthPrefix<MultipartHeaderDto>(stream, PrefixStyle.Base128, 1);

                    //if (partHeader.Encoding == KnownEncoding.ProtobufEncoding)
                    //{
                    //    using (var argStream = new ReadOnlyMemoryStream(content.Slice((int)stream.Position, partHeader.Size)))
                    //    {
                    //        try
                    //        {
                    //            args[i] = Serializer.NonGeneric.Deserialize(argType, argStream);
                    //        }
                    //        catch (Exception)
                    //        {
                    //            error = ErrorDeserializingArgument(action.ActionFullName, argIndex: i, argType);
                    //            return false;
                    //        }
                    //    }
                    //}
                    //else if (partHeader.Encoding == KnownEncoding.RawEncoding)
                    //{
                    //    ReadOnlyMemory<byte> raw = content.Slice((int)stream.Position, partHeader.Size);

                    //    if (!RawEncodingArg(raw, ref args[i], argType))
                    //    {
                    //        error = ErrorDeserializingArgument(action.ActionFullName, argIndex: i, argType);
                    //        return false;
                    //    }
                    //}
                    //stream.Position += partHeader.Size;
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

        private static InvalidRequestResult ErrorDeserializingArgument(string actionName, short argIndex, Type argType)
        {
            if (argType.IsClrType())
            {
                return new InvalidRequestResult($"Не удалось десериализовать аргумент №{argIndex} в тип {argType.Name} метода {actionName}");
            }
            else
            // Не будем раскрывать удалённой стороне имена сложных типов.
            {
                return new InvalidRequestResult($"Не удалось десериализовать аргумент №{argIndex} метода {actionName}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BadRequestResult ArgumentsCountMismatchError(string actionName, int targetArgumentsCount)
        {
            return new BadRequestResult(string.Format(CultureInfo.InvariantCulture, ArgumentsCountMismatch, actionName, targetArgumentsCount));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ValidateArgumentsCount(ParameterInfo[] targetArguments, short jsonArgsCount, string actionName, [MaybeNullWhen(true)] out IActionResult? error)
        {
            if (jsonArgsCount == targetArguments.Length)
            {
                error = null;
                return true;
            }
            else
            {
                error = ArgumentsCountMismatchError(actionName, targetArguments.Length);
                return false;
            }
        }

#if DEBUG
        [DebuggerDisplay("{ToString()}")]
        private readonly ref struct DebuggerDisplayJson
        {
            private readonly ReadOnlySpan<byte> _utf8Json;

            public DebuggerDisplayJson(ReadOnlySpan<byte> utf8Json)
            {
                _utf8Json = utf8Json;
            }

#if NETSTANDARD2_0 || NET472

#else
            public string AsIndented => ToIndentedString();

            public override string ToString()
            {
                return Encoding.UTF8.GetString(_utf8Json);
            }

            public string ToIndentedString()
            {
                string j = Encoding.UTF8.GetString(_utf8Json);
                var element = JsonDocument.Parse(j).RootElement;
                return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
            }
#endif
        }
#endif

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
            int? uid = null;
            int payloadLength = -1;
            string? actionName = null;
            string? contentEncoding = null;

            bool gotCode = false;
            bool gotUid = false;
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
                    else if (!gotUid && reader.ValueTextEquals("id"))
                    {
                        if (reader.Read())
                        {
                            uid = reader.GetInt32();
                            gotUid = true;
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
            return new HeaderDto(uid, statusCode, payloadLength, contentEncoding, actionName);
        }
    }
}
