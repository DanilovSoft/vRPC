using DanilovSoft.vRPC;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LoadTestApp
{
    public interface ITestController
    {
        Task<string> Ping(string msg);
    }

    class Program
    {
        private static int _connectionsCount;

        static void Main()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();

            int count = GetConnectionsCount();

            //ThreadPool.SetMinThreads(count, 1000);
            for (int i = 0; i < count; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(async delegate
                {
                    //int pauseMsec = new Random().Next(1_000, 2_000);
                    //await Task.Delay(pauseMsec);

                    VRpcClient cli = new VRpcClient("127.0.0.1", 1234, false, false);
                    var p = cli.GetProxy<ITestController>();
                    var res = await cli.ConnectExAsync();
                    Interlocked.Increment(ref _connectionsCount);
                    if (res.State == ConnectionState.Connected)
                    {
                        while (true)
                        {
                            string pong = await p.Ping("ping");
                            int pauseMsec = new Random().Next(1_000, 2_000);
                            await Task.Delay(pauseMsec);
                        }
                    }
                    else
                    {
                        Debugger.Break();
                    }
                }, null);
            }

            Console.Write("Connections Count: ");
            var pos = Console.CursorLeft;
            while (true)
            {
                Console.CursorLeft = pos;
                string sCount = Volatile.Read(ref _connectionsCount).ToString();
                Console.Write(sCount.PadRight(10));
                Console.CursorLeft = pos + sCount.Length;
                Thread.Sleep(200);
            }
        }

        private static int GetConnectionsCount()
        {
            string s;
            int count;
            do
            {
                Console.Write("Число соединений [1]: ");
                s = Console.ReadLine();
                if (s == "")
                    return 1;

            } while (!int.TryParse(s, out count));

            return count;
        }
    }
}
