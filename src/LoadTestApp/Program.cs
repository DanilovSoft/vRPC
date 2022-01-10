using DanilovSoft.vRPC;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LoadTestApp
{
    public interface ITestController
    {
        [JsonRpc]
        [TcpNoDelay]
        string Ping(string msg);

        [JsonRpc]
        [TcpNoDelay]
        Task<string> PingAsync(string msg);
    }

    class Program
    {
        private static int ConnectionsCount;
        private static VRpcClient[] Clients;
        private static int Port;

        static void Main()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            Port = listener.Port;

            var count = GetConnectionsCount();
            Clients = new VRpcClient[count];

            ThreadPool.QueueUserWorkItem(async delegate
            {
                for (var i = 0; i < count; i++)
                {
                    Clients[i] = CreateClient();
                    ThreadPool.UnsafeQueueUserWorkItem(s => ThreadEntry(s), i);
                }

                while (true)
                {
                    await Task.Delay(200);
                    var cli = Clients[new Random().Next(Clients.Length)];
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
                var sCount = Volatile.Read(ref ConnectionsCount).ToString();
                Console.Write(sCount.PadRight(10));
                Console.CursorLeft = pos + sCount.Length;
                Thread.Sleep(200);
            }
        }

        private static async void ThreadEntry(object state)
        {
            var index = (int)state;
            while (true)
            {
                var cli = Clients[index];
                var p = cli.GetProxy<ITestController>();
                ConnectResult res;
                try
                {
                    res = await cli.ConnectExAsync();
                }
                catch (VRpcException ex)
                {
                    await Task.Delay(100);
                    Clients[index] = CreateClient();
                    continue;
                }

                if (res.State == ConnectionState.Connected)
                {
                    Interlocked.Increment(ref ConnectionsCount);
                    var skipNextDelay = false;
                    while (cli.State == VRpcState.Open)
                    {
                        try
                        {
                            var pong = await p.PingAsync("ping");
                        }
                        catch (VRpcShutdownException)
                        {
                            Interlocked.Decrement(ref ConnectionsCount);
                            await Task.Delay(100);
                            Clients[index] = CreateClient();
                            skipNextDelay = true;
                            break;
                        }
                        //await Task.Delay(100);
                    }

                    if (!skipNextDelay)
                    {
                        Interlocked.Decrement(ref ConnectionsCount);
                        Clients[index] = CreateClient();
                    }
                }
                else if (res.State == ConnectionState.ShutdownRequest)
                {
                    await Task.Delay(100);
                    Clients[index] = CreateClient();
                }
                else
                {
                    await Task.Delay(100);
                }
            }
        }

        private static VRpcClient CreateClient()
        {
            return new VRpcClient("127.0.0.1", Port, false, false);
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
