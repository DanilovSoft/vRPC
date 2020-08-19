using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DanilovSoft.vRPC
{
    internal static class JsonRpcSerializer
    {
        private static readonly JsonEncodedText JsonRpcVersion = JsonEncodedText.Encode("jsonrpc");
        private static readonly JsonEncodedText Method = JsonEncodedText.Encode("method");
        private static readonly JsonEncodedText Parameters = JsonEncodedText.Encode("params");
        private static readonly JsonEncodedText Id = JsonEncodedText.Encode("id");

        public static void SerializeRequest(IBufferWriter<byte> bufferWriter, string method, object[] args, int uid)
        {
            //int initialPosition = bufferWriter.WrittenCount;

            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                // {
                writer.WriteStartObject();

                // jsonrpc: "2.0"
                writer.WriteString(JsonRpcVersion, "2.0");

                // method: "..."
                writer.WriteString(Method, method);

                // params: [
                writer.WriteStartArray(Parameters);

                for (int i = 0; i < args.Length; i++)
                {
                    JsonSerializer.Serialize(writer, args[i]);
                }

                // ]
                writer.WriteEndArray();

                // Id: 1
                writer.WriteNumber(Id, uid);

                // }
                writer.WriteEndObject();
            }
        }

        internal static bool TryDeserialize(ReadOnlySpan<byte> utf8Json, InvokeActionsDictionary invokeMethods, [MaybeNullWhen(true)] out IActionResult? error)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            string? actionName = null;
            string? id = null;
            ControllerMethodMeta? methodMeta = null;

            bool gotMethod = false;
            bool gotId = false;
            JsonReaderState paramsState;

            var reader = new Utf8JsonReader(utf8Json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (!gotMethod && reader.ValueTextEquals("method"))
                    {
                        if (reader.Read())
                        {
                            actionName = reader.GetString();
                            gotMethod = true;

                            if (!invokeMethods.TryGetAction(actionName, out methodMeta))
                            {
                                error = default;
                                return false;
                            }
                        }
                    }
                    else if (!gotId && reader.ValueTextEquals("id"))
                    {
                        if (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.Number)
                            {
                                id = reader.GetDouble().ToString(CultureInfo.InvariantCulture);
                                gotId = true;
                            }
                            else
                            {
                                id = reader.GetString();
                            }
                            gotId = true;
                        }
                    }
                    else if (reader.ValueTextEquals("params"))
                    {
                        if (methodMeta != null)
                        {
                            ReadArgs(methodMeta, reader);
                        }
                        else
                        {
                            paramsState = reader.CurrentState;
                        }
                    }
                }
            }

            error = default;
            return true;
        }

        private static void ReadArgs(ControllerMethodMeta method, Utf8JsonReader reader)
        {
            if (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    object[] args = method.Parametergs.Length == 0
                        ? Array.Empty<object>()
                        : (new object[method.Parametergs.Length]);

                    // Считаем сколько аргументов есть в json'е.
                    short argsInJsonCounter = 0;

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
                                //result = default;
                                //error = ErrorDeserializingArgument(method.MethodFullName, argIndex: argsInJsonCounter, paramType);
                                //return false;
                            }
                            argsInJsonCounter++;
                        }
                        else
                        // Несоответствие числа параметров.
                        {
                            //result = default;
                            //error = ArgumentsCountMismatchError(method.MethodFullName, method.Parametergs.Length);
                            //return false;
                        }
                    }
                }
            }
        }
    }
}
