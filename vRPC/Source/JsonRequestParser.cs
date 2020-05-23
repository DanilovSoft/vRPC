using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace DanilovSoft.vRPC
{
    internal static class JsonRequestParser
    {
        /// <summary>
        /// Десериализует json запрос.
        /// </summary>
        public static RequestToInvoke? TryDeserializeRequestJson(ReadOnlySpan<byte> utf8Json, InvokeActionsDictionary invokeActions, HeaderDto header, out IActionResult? error)
        {
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
                                    if (!invokeActions.TryGetAction(actionName, out action))
                                    {
#if NETSTANDARD2_0 || NET472
                                        int controllerIndex = actionName.IndexOf(GlobalVars.ControllerNameSplitter);
#else
                                        int controllerIndex = actionName.IndexOf(GlobalVars.ControllerNameSplitter, StringComparison.Ordinal);
#endif
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
                                    else
                                    {
                                        targetArguments = action.TargetMethod.GetParameters();
                                    }
                                }
                            }
                        }
                        else if (reader.ValueTextEquals("a"))
                        // В json'е есть параметры для метода.
                        {
                            hasArguments = true;

                            if (action != null)
                            {
                                if (reader.Read())
                                {
                                    if (reader.TokenType == JsonTokenType.StartArray)
                                    {
                                        // Считает сколько аргументов было в json'е и используется как индекс.
                                        short jsonArgsCount = 0;

                                        if(targetArguments.Length == 0)
                                            args = Array.Empty<object>();
                                        else
                                            args = new object[targetArguments.Length];

                                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                        {
                                            Type type = targetArguments[jsonArgsCount].ParameterType;

                                            try
                                            {
                                                args[jsonArgsCount] = JsonSerializer.Deserialize(ref reader, type);
                                            }
                                            catch (Exception ex)
                                            {
                                                throw new InvalidOperationException($"Ошибка при десериализации аргумента №{jsonArgsCount + 1} для метода '{actionName}'", ex);
                                            }
                                            jsonArgsCount++;
                                        }

                                        if (!ValidateArgumentsCount(targetArguments, jsonArgsCount, actionName, out error))
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
                if(args == null)
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

        private static bool ValidateArgumentsCount(ParameterInfo[] targetArguments, short jsonArgsCount, string actionName, out IActionResult? error)
        {
            Debug.Assert(actionName != null);
            if (jsonArgsCount == targetArguments.Length)
            {
                error = null;
                return true;
            }
            else
            {
                error = new BadRequestResult($"Argument count mismatch for action '{actionName}'.");
                return false;
            }
        }

        ///// <summary>
        ///// Производит маппинг аргументов запроса в соответствии с делегатом.
        ///// </summary>
        ///// <param name="method">Метод который будем вызывать.</param>
        //private object[] DeserializeParameters(ParameterInfo[] targetArguments, RequestMessage request)
        //{
        //    object[] args = new object[targetArguments.Length];

        //    for (int i = 0; i < targetArguments.Length; i++)
        //    {
        //        ParameterInfo p = targetArguments[i];
        //        var arg = request.Args.FirstOrDefault(x => x.ParameterName.Equals(p.Name, StringComparison.InvariantCultureIgnoreCase));
        //        if (arg == null)
        //            throw new BadRequestException($"Argument \"{p.Name}\" missing.");

        //        args[i] = arg.Value.ToObject(p.ParameterType);
        //    }
        //    return args;
        //}
    }
}
