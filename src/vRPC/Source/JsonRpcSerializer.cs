using DanilovSoft.vRPC.JsonRpc;
using DanilovSoft.vRPC.Source;
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

        internal static bool TryDeserialize(ReadOnlySpan<byte> utf8Json, InvokeActionsDictionary invokeMethods, out JsonRequest result, [MaybeNullWhen(true)] out IActionResult? error)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            string? actionName = null;
            //int? id = null;
            //ControllerMethodMeta? method = null;

            result = new JsonRequest();

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

                            if (invokeMethods.TryGetAction(actionName, out var method))
                            {
                                result.Method = method;

                                result.Args = method.Parametergs.Length == 0
                                    ? Array.Empty<object>()
                                    : (new object[method.Parametergs.Length]);
                            }
                            else
                            {
                                result = default;
                                error = ResponseHelper.MethodNotFound(actionName);
                                return false;
                            }
                        }
                    }
                    else if (!gotId && reader.ValueTextEquals("id"))
                    {
                        if (reader.Read())
                        {
                            // TODO формат может быть String или Number.
                            result.Id = reader.GetInt32();
                            gotId = true;
                        }
                    }
                    else if (reader.ValueTextEquals("params"))
                    {
                        if (result.Method != null)
                        {
                            if (!TryReadArgs(result.Method, result.Args!, reader, out error))
                            {
                                result = default;
                                return false;
                            }
                        }
                        else
                        {
                            paramsState = reader.CurrentState;
                            // TODO
                        }
                    }
                }
            }


            error = default;
            return true;
        }

        private static bool TryReadArgs(ControllerMethodMeta method, object[] args, Utf8JsonReader reader, [MaybeNullWhen(true)] out IActionResult? error)
        {
            if (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
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
                                error = ResponseHelper.ErrorDeserializingArgument(method.MethodFullName, argIndex: argsInJsonCounter, paramType);
                                return false;
                            }
                            argsInJsonCounter++;
                        }
                        else
                        // Несоответствие числа параметров.
                        {
                            error = ResponseHelper.ArgumentsCountMismatchError(method.MethodFullName, method.Parametergs.Length);
                            return false;
                        }
                    }
                }
            }
            error = default;
            return true;
        }
    }
}
