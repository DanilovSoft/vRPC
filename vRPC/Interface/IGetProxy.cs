using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal interface IGetProxy
    {
        T GetProxy<T>();
    }
}
