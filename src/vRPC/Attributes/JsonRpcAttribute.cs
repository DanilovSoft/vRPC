using System;

namespace DanilovSoft.vRPC
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class JsonRpcAttribute : Attribute
    {
        public JsonRpcAttribute()
        {

        }
    }
}
