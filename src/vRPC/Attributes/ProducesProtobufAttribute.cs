using System;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProducesProtoBufAttribute : Attribute
    {
        public const string Encoding = KnownEncoding.ProtobufEncoding;
    }
}
