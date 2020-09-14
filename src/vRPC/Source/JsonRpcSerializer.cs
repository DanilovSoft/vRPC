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
        internal static readonly JsonEncodedText JsonRpcVersion = JsonEncodedText.Encode("jsonrpc");
        internal static readonly JsonEncodedText JsonRpcVersionValue = JsonEncodedText.Encode("2.0");
        internal static readonly JsonEncodedText Method = JsonEncodedText.Encode("method");
        internal static readonly JsonEncodedText Error = JsonEncodedText.Encode("error");
        internal static readonly JsonEncodedText Code = JsonEncodedText.Encode("code");
        internal static readonly JsonEncodedText Message = JsonEncodedText.Encode("message");
        internal static readonly JsonEncodedText Params = JsonEncodedText.Encode("params");
        internal static readonly JsonEncodedText Result = JsonEncodedText.Encode("result");
        internal static readonly JsonEncodedText Id = JsonEncodedText.Encode("id");

        /// <exception cref="JsonException"/>
        internal static void SerializeRequest(ArrayBufferWriter<byte> buffer, string method, object[] args, int id)
        {
            using (var writer = new Utf8JsonWriter(buffer))
            {
                // {
                writer.WriteStartObject();

                // jsonrpc: "2.0"
                writer.WriteString(JsonRpcVersion, JsonRpcVersionValue);

                // method: "..."
                writer.WriteString(Method, method);

                // params: [
                writer.WriteStartArray(Params);

                for (int i = 0; i < args.Length; i++)
                {
                    JsonSerializer.Serialize(writer, args[i]);
                }

                // ]
                writer.WriteEndArray();

                // Id: 1
                writer.WriteNumber(Id, id);

                // }
                writer.WriteEndObject();
            }
        }

        /// <remarks>Не бросает исключения.</remarks>
        internal static bool TrySerializeRequest(string method, object[] args, int id, out ArrayBufferWriter<byte> buffer,
            [NotNullWhen(false)] out VRpcSerializationException? exception)
        {
            buffer = new ArrayBufferWriter<byte>();
            bool dispose = true;
            try
            {
                SerializeRequest(buffer, method, args, id);

                dispose = false; // Предотвратить Dispose.
                exception = null;
                return true;
            }
            catch (Exception ex)
            {
                exception = new VRpcSerializationException("Ошибка при сериализации пользовательских данных.", ex);
                return false;
            }
            finally
            {
                if (dispose)
                    buffer.Dispose();
            }
        }

        internal static void SerializeResponse(ArrayBufferWriter<byte> buffer, int id, object? result)
        {
            // {"jsonrpc": "2.0", "result": 19, "id": 1}
            using (var writer = new Utf8JsonWriter(buffer))
            {
                // {
                writer.WriteStartObject();

                // jsonrpc: "2.0"
                writer.WriteString(JsonRpcVersion, JsonRpcVersionValue);

                // Id: 1
                writer.WriteNumber(Id, id);

                if (result != null)
                {
                    // result: "..."
                    writer.WritePropertyName(Result);

                    JsonSerializer.Serialize(writer, result, result.GetType());
                }
                else
                {
                    writer.WriteNull(Result);
                }

                // }
                writer.WriteEndObject();
            }
        }

        internal static void SerializeErrorResponse(ArrayBufferWriter<byte> buffer, StatusCode code, string message, int? id)
        {
            // {"jsonrpc": "2.0", "id": "1", "error": {"code": -32601, "message": "Method not found"}}
            using (var writer = new Utf8JsonWriter(buffer))
            {
                // {
                writer.WriteStartObject();

                // "jsonrpc": "2.0"
                writer.WriteString(JsonRpcVersion, JsonRpcVersionValue);

                if (id != null)
                {
                    // Id: 1
                    writer.WriteNumber(Id, id.Value);
                }
                else
                // По стандарту мы должны записать Null если не удалось получить id запроса.
                {
                    writer.WriteNull(Id);
                }

                // "error": "{"
                writer.WriteStartObject(Error);

                // "code": -32601
                writer.WriteNumber(Code, (int)code);

                // "message": "..."
                writer.WriteString(Message, message);

                // "}"
                writer.WriteEndObject();

                // }
                writer.WriteEndObject();
            }
        }

        internal static void SerializeNotification(ArrayBufferWriter<byte> buffer, string method, object[] args)
        {

        }

        internal static bool TrySerializeNotification(string method, object[] args,
            [NotNullWhen(true)] out ArrayBufferWriter<byte> buffer,
            [NotNullWhen(false)] out VRpcSerializationException? exception)
        {
            buffer = new ArrayBufferWriter<byte>();
            bool dispose = true;
            try
            {
                SerializeNotification(buffer, method, args);

                dispose = false; // Предотвратить Dispose.
                exception = null;
                return true;
            }
            catch (Exception ex)
            {
                exception = new VRpcSerializationException("Ошибка при сериализации пользовательских данных.", ex);
                return false;
            }
            finally
            {
                if (dispose)
                    buffer.Dispose();
            }
        }
    }
}
