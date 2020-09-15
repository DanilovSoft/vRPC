using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC
{
    internal interface IJRequest : IRequest
    {
        bool TrySerialize(out ArrayBufferWriter<byte> buffer);
    }
}
