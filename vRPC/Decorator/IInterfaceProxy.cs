using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC.Decorator
{
    internal interface IInterfaceProxy
    {
        T Clone<T>() where T : IInterfaceProxy;
    }
}
