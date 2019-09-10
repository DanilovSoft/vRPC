using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay("{Value}")]
    internal readonly struct Arg
    {
        public JToken Value { get; }

        public Arg(object arg)
        {
            Value = arg == null ? null : JToken.FromObject(arg);
        }
    }
}
