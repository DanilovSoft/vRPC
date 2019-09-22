using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    using System.Diagnostics;
    using System.Reflection;
//#if NETCOREAPP3_0

    using System.Text.Json;

    internal static class JsonRequestParser
    {
        /// <summary>
        /// Десериализует json запрос.
        /// </summary>
        public static RequestToInvoke TryDeserializeRequestJson(ReadOnlySpan<byte> utf8Json, InvokeActionsDictionary invokeActions, HeaderDto header, out IActionResult error)
        {
            ControllerAction action = null;
            object[] args = null;
            ParameterInfo[] targetArguments = null;
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
                                    string actionName = reader.GetString();
                                    if (!invokeActions.TryGetAction(actionName, out action))
                                    {
                                        error = new NotFoundResult($"Unable to find requested action \"{actionName}\".");
                                        return default;
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
                                        int argIndex = 0;
                                        List<object> argsList = null;
                                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                                        {
                                            if (argsList == null)
                                                argsList = new List<object>(1);

                                            Type type = targetArguments[argIndex].ParameterType;
                                            argsList.Add(JsonSerializer.Deserialize(ref reader, type));
                                            argIndex++;
                                        }
                                        args = argsList?.ToArray() ?? Array.Empty<object>();
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
                        return default;
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
    }
//#endif
}
