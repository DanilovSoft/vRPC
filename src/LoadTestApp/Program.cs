using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
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
        private static VRpcClient[] _clients;

        static void Main()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();

            int count = GetConnectionsCount();
            _clients = new VRpcClient[count];

            ThreadPool.QueueUserWorkItem(async delegate
            {
                for (int i = 0; i < count; i++)
                {
                    var cli = _clients[i] = CreateClient();
                    ThreadPool.UnsafeQueueUserWorkItem(s => ThreadEntry(s), i);
                }

                while (true)
                {
                    await Task.Delay(200);
                    var cli = _clients[new Random().Next(_clients.Length)];
                    if (cli.State == VRpcState.Open)
                    {
                        var result = cli.Shutdown(TimeSpan.Zero, "Провоцируем обрыв");
                        cli.Dispose();
                    }
                    //cli.Dispose();
                }
            });

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

        private static async void ThreadEntry(object state)
        {
            int index = (int)state;
            while (true)
            {
                var cli = _clients[index];
                var p = cli.GetProxy<ITestController>();
                ConnectResult res;
                try
                {
                    res = await cli.ConnectExAsync();
                }
                catch (VRpcException ex)
                {
                    await Task.Delay(100);
                    _clients[index] = CreateClient();
                    continue;
                }

                if (res.State == ConnectionState.Connected)
                {
                    Interlocked.Increment(ref _connectionsCount);
                    bool skipNextDelay = false;
                    while (cli.State == VRpcState.Open)
                    {
                        try
                        {
                            string pong = await p.Ping("ping");
                        }
                        catch (VRpcWasShutdownException ex)
                        {
                            Interlocked.Decrement(ref _connectionsCount);
                            await Task.Delay(100);
                            _clients[index] = CreateClient();
                            skipNextDelay = true;
                            break;
                        }
                        await Task.Delay(100);
                    }

                    if (!skipNextDelay)
                    {
                        Interlocked.Decrement(ref _connectionsCount);
                        _clients[index] = CreateClient();
                    }
                }
                else if (res.State == ConnectionState.ShutdownRequest)
                {
                    await Task.Delay(100);
                    _clients[index] = CreateClient();
                }
                else
                {
                    await Task.Delay(100);
                }
            }
        }

        private static VRpcClient CreateClient()
        {
            return new VRpcClient("127.0.0.1", 1234, false, false);
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
