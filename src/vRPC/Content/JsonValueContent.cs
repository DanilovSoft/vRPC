using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    public class JsonValueContent : VRpcContent
    {
        private readonly object _value;

        public JsonValueContent(object? value)
        {
            _value = value;
        }

        protected internal sealed override bool TryComputeLength(out int length)
        {
            length = -1;
            return false;
        }

        internal override ReadOnlyMemory<byte> GetMemory()
        {
            throw new NotImplementedException();
        }

        private protected sealed override Multipart SerializeToStream(IBufferWriter<byte> writer)
        {
            if (_value != null)
            {
                using (var jsonWriter = new Utf8JsonWriter(writer))
                {
                    JsonSerializer.Serialize(jsonWriter, _value, _value.GetType());
                    jsonWriter.Flush();
                    SerializeHeader(writer, (int)jsonWriter.BytesCommitted, KnownEncoding.BinaryEncoding);
                }
            }
            throw new NotImplementedException();
        }
    }
}
