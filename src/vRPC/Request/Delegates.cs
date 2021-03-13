using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal delegate void TrySetJResponseDelegate(ref Utf8JsonReader reader);
    internal delegate void TrySetVResponseDelegate(in HeaderDto header, ReadOnlyMemory<byte> payload);
}
