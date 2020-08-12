using DanilovSoft.vRPC;
using System;
using System.Net;
using System.Threading;

namespace ServerApp
{
    class Program
    {
        static void Main()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();
            Thread.Sleep(-1);
        }
    }
}
