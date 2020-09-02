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
        internal static readonly JsonEncodedText JsonRpcVersion = JsonEncodedText.Encode("jsonrpc");
        internal static readonly JsonEncodedText Method = JsonEncodedText.Encode("method");
        internal static readonly JsonEncodedText Error = JsonEncodedText.Encode("error");
        internal static readonly JsonEncodedText Code = JsonEncodedText.Encode("code");
        internal static readonly JsonEncodedText Message = JsonEncodedText.Encode("message");
        internal static readonly JsonEncodedText Params = JsonEncodedText.Encode("params");
        internal static readonly JsonEncodedText Result = JsonEncodedText.Encode("result");
        internal static readonly JsonEncodedText Id = JsonEncodedText.Encode("id");

        public static void SerializeRequest(IBufferWriter<byte> bufferWriter, string method, object[] args, int id)
        {
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                // {
                writer.WriteStartObject();

                // jsonrpc: "2.0"
                writer.WriteString(JsonRpcVersion, "2.0");

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

        public static void SerializeErrorResponse(IBufferWriter<byte> bufferWriter, StatusCode code, string message, int? id)
        {
            // {"jsonrpc": "2.0", "error": {"code": -32601, "message": "Method not found"}, "id": "1"}
            using (var writer = new Utf8JsonWriter(bufferWriter))
            {
                // {
                writer.WriteStartObject();

                // "jsonrpc": "2.0"
                writer.WriteString(JsonRpcVersion, "2.0");

                // "error": "{"
                writer.WriteStartObject(Error);

                // "code": -32601
                writer.WriteNumber(Code, (int)code);

                // "message": "..."
                writer.WriteString(Message, message);

                // "}"
                writer.WriteEndObject();

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

                // }
                writer.WriteEndObject();
            }
        }
    }
}
