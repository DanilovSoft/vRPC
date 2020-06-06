using DanilovSoft.vRPC.Decorator;
using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal interface IGetProxy
    {
        TIface GetProxy<TIface>() where TIface : class;
        //IInterfaceDecorator<TIface> GetProxyDecorator<TIface>() where TIface : class;
    }
}
