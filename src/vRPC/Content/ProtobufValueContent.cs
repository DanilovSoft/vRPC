using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.DTO;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    public class ProtobufValueContent : VRpcContent
    {
        private readonly object _value;

        public ProtobufValueContent(object value)
        {
            _value = value;
        }

        private protected override Multipart SerializeToStream(Stream stream)
        {
            int contentLength = (int)stream.Position;
            ProtoBuf.Serializer.NonGeneric.Serialize(stream, _value);
            
            int headerPosition = (int)stream.Position;

            contentLength = headerPosition - contentLength;

            ProtoBuf.Serializer.NonGeneric.Serialize(stream, new MultipartHeaderDto(contentLength, ProducesProtoBufAttribute.Encoding));
            byte headerSize = (byte)((int)stream.Position - headerPosition);

            return new Multipart(contentLength, headerSize);
        }

        protected internal override bool TryComputeLength(out int length)
        {
            length = -1;
            return false;
        }
    }
}
