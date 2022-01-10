using System;

namespace DanilovSoft.vRPC
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProducesAttribute : Attribute
    {
        public string ContentType { get; }

        public ProducesAttribute(string contentType)
        {
            ContentType = contentType;
        }
    }
}
