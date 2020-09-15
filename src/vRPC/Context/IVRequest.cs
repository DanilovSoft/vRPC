using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC
{
    internal interface IVRequest : IRequest
    {
        bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize);
    }
}
