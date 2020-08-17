using System;
using System.Text;
using System.Diagnostics;
using System.Text.Json;

namespace DanilovSoft.vRPC
{
#if DEBUG
    [DebuggerDisplay("{ToString()}")]
    internal readonly ref struct DebuggerDisplayJson
    {
        private readonly ReadOnlySpan<byte> _utf8Json;

        public DebuggerDisplayJson(ReadOnlySpan<byte> utf8Json)
        {
            _utf8Json = utf8Json;
        }

#if NETSTANDARD2_0 || NET472

#else
        public string AsIndented => ToIndentedString();

        public override string ToString()
        {
            return Encoding.UTF8.GetString(_utf8Json);
        }

        public string ToIndentedString()
        {
            string j = Encoding.UTF8.GetString(_utf8Json);
            var element = JsonDocument.Parse(j).RootElement;
            return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
        }
#endif
    }
#endif
}
