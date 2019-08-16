using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProducesProtoBufAttribute : Attribute
    {
        public const string Encoding = "protobuf";
    }
}
