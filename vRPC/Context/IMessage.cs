using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    internal interface IMessage
    {
        JToken[] Args { get; }
    }
}
