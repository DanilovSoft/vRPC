using DanilovSoft.vRPC;
using DanilovSoft.WebSockets;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Test();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(-1);
        }

        static void Test()
        {
            var cts = new CancellationTokenSource(10000);
            var server = new RpcListener(IPAddress.Any, 1234);
            var task = server.RunAsync(TimeSpan.FromSeconds(3), null, cts.Token);
            server = null;
        }
    }
}
