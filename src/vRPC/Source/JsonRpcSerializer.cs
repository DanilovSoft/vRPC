using System;
using System.Buffers;
using System.Collections.Generic;
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

        public static bool TryDeserialize(ReadOnlySpan<byte> utf8Json)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            string? actionName = null;
            int? id = null;

            bool gotMethod = false;
            bool gotId = false;

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
                        }
                    }
                    else if (!gotId && reader.ValueTextEquals("id"))
                    {
                        if (reader.Read())
                        {
                            id = reader.GetInt32();
                            gotId = true;
                        }
                    }
                }
            }

            throw new NotImplementedException();
        }
    }
}
