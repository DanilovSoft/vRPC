using DanilovSoft.vRPC;
using System;
using System.Net;

namespace Server
{
    class Program
    {
        static void Main()
        {
            var listener = new RpcListener(IPAddress.Any, 1234);
            listener.RunAsync().Wait();
        }
    }
}
