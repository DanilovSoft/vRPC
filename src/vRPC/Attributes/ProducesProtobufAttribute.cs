using System;
using System.Collections.Generic;
using System.Text;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProducesProtoBufAttribute : Attribute
    {
        public const string Encoding = KnownEncoding.ProtobufEncoding;
    }
}
