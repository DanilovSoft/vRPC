using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal interface IMessage
    {
        JToken[] Args { get; }
    }
}
