using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;

namespace DanilovSoft.vRPC
{
    public class ProtobufValueContent : VRpcContent
    {
        private readonly object _value;

        public ProtobufValueContent(object value)
        {
            _value = value;
        }

        protected internal override Task SerializeToStreamAsync(Stream stream)
        {
            ProtoBuf.Serializer.NonGeneric.Serialize(stream, _value);

            return Task.CompletedTask;
        }

        protected internal override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
