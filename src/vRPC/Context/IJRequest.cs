using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC
{
    internal interface IJRequest : IRequest
    {
        bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer);
    }
}
