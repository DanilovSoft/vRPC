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
        private static List<VRpcClient> _list;

        static void Main()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();

            int count = GetConnectionsCount();

            _list = new List<VRpcClient>(count);
            for (int i = 0; i < count; i++)
            {
                var cli = CreateClient();
                _list.Add(cli);
            }

            for (int i = 0; i < count; i++)
            {
                ThreadPool.QueueUserWorkItem(async s =>
                {
                    int index = (int)s;
                    while (true)
                    {
                        var cli = _list[index];
                        var p = cli.GetProxy<ITestController>();
                        ConnectResult res;
                        try
                        {
                            res = await cli.ConnectExAsync();
                        }
                        catch (Exception ex)
                        {
                            await Task.Delay(new Random().Next(3_000, 5_000));
                            _list[index] = CreateClient();
                            continue;
                        }

                        if (res.State == ConnectionState.Connected)
                        {
                            Interlocked.Increment(ref _connectionsCount);
                            bool skip = false;
                            while (cli.State == VRpcState.Open)
                            {
                                try
                                {
                                    string pong = await p.Ping("ping");
                                }
                                catch (VRpcWasShutdownException ex)
                                {
                                    Interlocked.Decrement(ref _connectionsCount);
                                    await Task.Delay(new Random().Next(1_000, 2_000));
                                    _list[index] = CreateClient();
                                    skip = true;
                                    break;
                                }
                                await Task.Delay(new Random().Next(1_000, 2_000));
                            }

                            if (!skip)
                            {
                                Interlocked.Decrement(ref _connectionsCount);
                                _list[index] = CreateClient();
                            }
                        }
                        else if (res.State == ConnectionState.ShutdownRequest)
                        {
                            
                        }
                        else
                        {
                            await Task.Delay(new Random().Next(3_000, 5_000));
                        }
                    }
                }, i);
            }

            ThreadPool.QueueUserWorkItem(async delegate 
            {
                while (true)
                {
                    await Task.Delay(100);
                    var cli = _list[new Random().Next(_list.Count)];
                    var result = cli.Shutdown(TimeSpan.Zero, "Провоцируем обрыв");
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
