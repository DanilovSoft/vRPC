using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class JsonRpcAttribute : Attribute
    {
    }
}
