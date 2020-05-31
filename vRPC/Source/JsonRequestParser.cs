using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DanilovSoft.vRPC
{
    internal static class JsonRequestParser
    {
        private const string ArgumentsCountMismatch = "Argument count mismatch for action '{0}'. {1} arguments was expected.";

        /// <summary>
        /// Десериализует json запрос.
        /// </summary>
        public static RequestToInvoke? TryDeserializeRequestJson(ReadOnlySpan<byte> utf8Json, InvokeActionsDictionary invokeActions, HeaderDto header, out IActionResult? error)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif

            string? actionName = null;
            ControllerActionMeta? action = null;
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
                        if (action == null && reader.ValueTextEquals("n"))
                        {
                            if (reader.Read())
                            {
                                if (reader.TokenType == JsonTokenType.String)
                                {
                                    actionName = reader.GetString();
                                    if (invokeActions.TryGetAction(actionName, out action))
                                    {
                                        targetArguments = action.TargetMethod.GetParameters();
                                    }
                                    else
                                    {
                                        int controllerIndex = actionName.IndexOf(GlobalVars.ControllerNameSplitter, StringComparison.Ordinal);

                                        if (controllerIndex > 0)
                                        {
                                            error = new NotFoundResult($"Unable to find requested action \"{actionName}\".");
                                            return null;
                                        }
                                        else
                                        {
                                            error = new NotFoundResult($"Controller name not specified in request \"{actionName}\".");
                                            return null;
                                        }
                                    }
                                }
                            }
                        }
                        else if (reader.ValueTextEquals("a"))
                        // В json'е есть параметры для метода.
                        {
                            hasArguments = true;

                            if (targetArguments != null)
                            {
                                Debug.Assert(actionName != null, "Не может быть Null потому что targetArguments не Null");

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
                                                Type type = targetArguments[argsInJsonCounter].ParameterType;

                                                try
                                                {
                                                    args[argsInJsonCounter] = JsonSerializer.Deserialize(ref reader, type);
                                                }
                                                catch (Exception ex)
                                                {
                                                    throw new InvalidOperationException($"Ошибка при десериализации аргумента №{argsInJsonCounter + 1} для метода '{actionName}'", ex);
                                                }
                                                argsInJsonCounter++;
                                            }
                                            else
                                            // Выход за границы массива.
                                            {
                                                error = ArgumentsCountMismatchError(actionName, targetArguments.Length);
                                                return null;
                                            }
                                        }

                                        if (!ValidateArgumentsCount(targetArguments, argsInJsonCounter, actionName, out error))
                                        // Не соответствует число аргументов.
                                        {
                                            return null;
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
                        return null;
                    }
                }

                error = null;
                return new RequestToInvoke(header.Uid, action, args);
            }
            else
            {
                error = new BadRequestResult("В запросе остутствует имя вызываемого метода.");
                return default;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static BadRequestResult ArgumentsCountMismatchError(string actionName, int targetArgumentsCount)
        {
            return new BadRequestResult(string.Format(CultureInfo.InvariantCulture, ArgumentsCountMismatch, actionName, targetArgumentsCount));
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
                error = ArgumentsCountMismatchError(actionName, targetArguments.Length);
                return false;
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
