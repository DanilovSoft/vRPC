using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Decorator
{
    internal interface IInterfaceProxy
    {
        T Clone<T>() where T : class, IInterfaceProxy;
    }
}
