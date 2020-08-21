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
        private static readonly JsonEncodedText Error = JsonEncodedText.Encode("error");
        private static readonly JsonEncodedText Code = JsonEncodedText.Encode("code");
        private static readonly JsonEncodedText Message = JsonEncodedText.Encode("message");
        private static readonly JsonEncodedText Parameters = JsonEncodedText.Encode("params");
        private static readonly JsonEncodedText Id = JsonEncodedText.Encode("id");

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
                writer.WriteStartArray(Parameters);

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

        public static void SerializeResponse(IBufferWriter<byte> bufferWriter)
        {
            // {"jsonrpc": "2.0", "error": {"code": -32601, "message": "Method not found"}, "id": "1"}
        }

        public static void SerializeErrorResponse(IBufferWriter<byte> bufferWriter, StatusCode code, string message, int id)
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

                // Id: 1
                writer.WriteNumber(Id, id);

                // }
                writer.WriteEndObject();
            }
        }
    }
}
