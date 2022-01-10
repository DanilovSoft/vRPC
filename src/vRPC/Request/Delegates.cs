using System;
using System.Text.Json;

namespace DanilovSoft.vRPC
{
    internal delegate void TrySetJResponseDelegate(ref Utf8JsonReader reader);
    internal delegate void TrySetVResponseDelegate(in HeaderDto header, ReadOnlyMemory<byte> payload);
}
