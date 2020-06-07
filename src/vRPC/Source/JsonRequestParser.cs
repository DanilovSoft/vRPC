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

namespace DanilovSoft.vRPC
{
    internal static class RequestContentParser
    {
        private const string ArgumentsCountMismatch = "Argument count mismatch for action '{0}'. {1} arguments was expected.";

        /// <remarks>Не бросает исключения.</remarks>
        public static bool TryDeserializeRequest(ReadOnlyMemory<byte> content, ControllerActionMeta action,
            HeaderDto header,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(false)]
#endif
            out RequestToInvoke? result,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(true)]
#endif
            out IActionResult? error)
        {
            try
            {
                if (header.ContentEncoding != KnownEncoding.MultipartEncoding)
                {
                    return TryDeserializeRequestJson(content.Span, action, header, out result, out error);
                }
                else
                {
                    return TryDeserializeMultipart(content, action, header, out result, out error);
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
                    result = null;
                    return false;
                }
                else
                {
                    error = null;
                    result = null;
                    return false;
                }
            }
        }

        ///// <summary>
        ///// Если запрос ожидает результат то отправляет ошибку как результат.
        ///// </summary>
        ///// <param name="header">Заголовок запроса.</param>
        ///// <param name="exception">Ошибка произошедшая при разборе запроса.</param>
        //private static void TryPostSendErrorResponse(HeaderDto header, Exception exception)
        //{
        //    if (header.Uid != null)
        //    // Запрос ожидает ответ.
        //    {
        //        // Подготовить ответ с ошибкой.
        //        var errorResponse = new ResponseMessage(header.Uid.Value, new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{exception.Message}\"."));

        //        // Передать на отправку результат с ошибкой через очередь.
        //        PostSendResponse(errorResponse);
        //    }
        //}

        /// <summary>
        /// Десериализует json запрос.
        /// </summary>
        /// <exception cref="JsonException"/>
        /// <returns>True если успешно десериализовали.</returns>
        private static bool TryDeserializeRequestJson(ReadOnlySpan<byte> utf8Json, ControllerActionMeta action,
            HeaderDto header,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(false)]
#endif
            out RequestToInvoke? result,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(true)]
#endif
            out IActionResult? error)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            //string? actionName = null;
            object[]? args = null;
            ParameterInfo[]? targetArguments = null;
            bool hasArguments = false;

            do
            {
                var reader = new Utf8JsonReader(utf8Json);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        //if (action == null && reader.ValueTextEquals("n"))
                        //{
                        //    if (reader.Read())
                        //    {
                        //        if (reader.TokenType == JsonTokenType.String)
                        //        {
                        //            actionName = reader.GetString();
                        //            if (invokeActions.TryGetAction(actionName, out action))
                        //            {
                        //                targetArguments = action.Parametergs;
                        //            }
                        //            else
                        //            // Не найден метод контроллера.
                        //            {
                        //                return MethodNotFound(actionName, out result, out error);
                        //            }
                        //        }
                        //    }
                        //}
                        //else 
                        if (reader.ValueTextEquals("a"))
                        // В json'е есть параметры для метода.
                        {
                            hasArguments = true;

                            if (targetArguments != null)
                            {
                                //Debug.Assert(actionName != null, "Не может быть Null потому что targetArguments не Null");

                                if (reader.Read())
                                {
                                    if (reader.TokenType == JsonTokenType.StartArray)
                                    {
                                        args = targetArguments.Length == 0 
                                            ? Array.Empty<object>() 
                                            : (new object[targetArguments.Length]);

                                        // Считаем сколько аргументов есть в json'е.
                                        short argsInJsonCounter = 0;

                                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                        {
                                            if (targetArguments.Length > argsInJsonCounter)
                                            {
                                                Type paramType = targetArguments[argsInJsonCounter].ParameterType;

                                                try
                                                {
                                                    args[argsInJsonCounter] = JsonSerializer.Deserialize(ref reader, paramType);
                                                }
                                                catch (JsonException)
                                                {
                                                    result = null;
                                                    error = ErrorDeserializingArgument(action.ActionFullName, argIndex: argsInJsonCounter, paramType);
                                                    return false;
                                                }
                                                argsInJsonCounter++;
                                            }
                                            else
                                            // Выход за границы массива.
                                            {
                                                result = null;
                                                return ArgumentsCountMismatchError(action.ActionFullName, targetArguments.Length, out error);
                                            }
                                        }

                                        if (!ValidateArgumentsCount(targetArguments, argsInJsonCounter, action.ActionFullName, out error))
                                        // Не соответствует число аргументов.
                                        {
                                            result = null;
                                            return false;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                }
            } while (action != null && args == null && hasArguments);

            if (action != null)
            // В json'е был найден метод.
            {
                Debug.Assert(targetArguments != null, "Не может быть Null потому что action не Null");

                if (args == null)
                // В json'е отсутвует массив параметров.
                {
                    if (targetArguments.Length == 0)
                    {
                        args = Array.Empty<object>();
                    }
                    else
                    {
                        error = new BadRequestResult("В запросе остутствуют требуемые аргументы вызываемого метода.");
                        result = null;
                        return false;
                    }
                }

                error = null;
                result = new RequestToInvoke(header.Uid, action, args, Array.Empty<IDisposable>());
                return true;
            }
            else
            {
                error = new BadRequestResult("В запросе остутствует имя вызываемого метода.");
                result = null;
                return false;
            }
        }

        /// <exception cref="Exception"/>
        private static bool TryDeserializeMultipart(ReadOnlyMemory<byte> content, ControllerActionMeta action,
            HeaderDto header,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(false)]
#endif
            out RequestToInvoke? result,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(true)]
#endif
            out IActionResult? error)
        {
            object[] args;
            IList<IDisposable>? disposableArgs;

            if (action.Parametergs.Length == 0)
            {
                args = Array.Empty<object>();
                disposableArgs = Array.Empty<IDisposable>();
            }
            else
            {
                args = new object[action.Parametergs.Length];

                int disposableArgsCount = action.Parametergs.Count(x => typeof(IDisposable).IsAssignableFrom(x.ParameterType));

                disposableArgs = disposableArgsCount == 0 
                    ? (IList<IDisposable>)Array.Empty<IDisposable>() 
                    : new List<IDisposable>(disposableArgsCount);
            }

            try
            {
                if (DeserializeArgs(content, action, args, disposableArgs, out error))
                {
                    result = new RequestToInvoke(header.Uid, action, args, disposableArgs);
                    disposableArgs = null; // Предотвратить Dispose.
                    error = null;
                    return true;
                }
                else
                {
                    result = null;
                    return false;
                }
            }
            finally
            {
                if (disposableArgs != null)
                {
                    for (int i = 0; i < disposableArgs.Count; i++)
                        disposableArgs[i].Dispose();
                }
            }
        }

        private static bool DeserializeArgs(ReadOnlyMemory<byte> content, ControllerActionMeta action, object[] args, IList<IDisposable> disposableArgs,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(true)]
#endif
            out IActionResult? error)
        {
            using (var stream = new ReadOnlyMemoryStream(content))
            {
                for (short i = 0; i < action.Parametergs.Length; i++)
                {
                    Type argType = action.Parametergs[i].ParameterType;

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
                                error = ErrorDeserializingArgument(action.ActionFullName, argIndex: i, argType);
                                return false;
                            }
                        }
                    }
                    else if (partHeader.Encoding == KnownEncoding.RawEncoding)
                    {
                        ReadOnlyMemory<byte> raw = content.Slice((int)stream.Position, partHeader.Size);

                        if (!RawEncodingArg(raw, ref args[i], argType, disposableArgs))
                        {
                            error = ErrorDeserializingArgument(action.ActionFullName, argIndex: i, argType);
                            return false;
                        }
                    }
                    stream.Position += partHeader.Size;
                }
            }
            error = null;
            return true;
        }

        private static bool RawEncodingArg(ReadOnlyMemory<byte> raw, ref object argv, Type argType, IList<IDisposable> disposableArgs)
        {
            if (argType == typeof(RentedMemory))
            {
                IMemoryOwner<byte>? shared = MemoryPool<byte>.Shared.Rent(raw.Length);
                raw.CopyTo(shared.Memory);
                var disposableMemory = new RentedMemory(shared, raw.Length);
                disposableArgs.Add(disposableMemory);
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

        internal static InvalidRequestResult ErrorDeserializingArgument(string actionName, short argIndex, Type argType)
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

        //private static bool MethodNotFound(string actionName, out RequestToInvoke? result, out IActionResult error)
        //{
        //    int controllerIndex = actionName.IndexOf(GlobalVars.ControllerNameSplitter, StringComparison.Ordinal);

        //    if (controllerIndex > 0)
        //    {
        //        error = new NotFoundResult($"Unable to find requested action \"{actionName}\".");
        //    }
        //    else
        //    {
        //        error = new NotFoundResult($"Controller name not specified in request \"{actionName}\".");
        //    }
        //    result = null;
        //    return false;
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ArgumentsCountMismatchError(string actionName, int targetArgumentsCount, out IActionResult error)
        {
            error = new BadRequestResult(string.Format(CultureInfo.InvariantCulture, ArgumentsCountMismatch, actionName, targetArgumentsCount));
            return false;
        }

#if NETSTANDARD2_0 || NET472
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ValidateArgumentsCount(ParameterInfo[] targetArguments, short jsonArgsCount, string actionName, out IActionResult? error)
        {
            if (jsonArgsCount == targetArguments.Length)
            {
                error = null;
                return true;
            }
            else
            {
                error = new BadRequestResult($"Argument count mismatch for action '{actionName}'. {targetArguments.Length} arguments expected.");
                return false;
            }
        }
#else
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
                return ArgumentsCountMismatchError(actionName, targetArguments.Length, out error);
            }
        }
#endif

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
        }
}
