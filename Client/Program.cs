using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using vRPC;

namespace Client
{
    class Program
    {
        private const int Port = 65125;

        static void Main()
        {
            Console.Title = "Клиент";
            Thread.Sleep(1000);
            long reqCount = 0;

            const int threads = 1;
            var ce = new CountdownEvent(threads);
            ThreadPool.SetMinThreads(threads, threads);
            for (int i = 0; i < threads; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(async delegate 
                {
                    using (var client = new vRPC.Client("127.0.0.1", Port))
                    {
                        client.ConfigureService(ioc =>
                        {
                            ioc.AddLogging(loggingBuilder =>
                            {
                                loggingBuilder
                                    .AddConsole();
                            });
                        });
                        var homeController = client.GetProxy<IHomeController>();

                        while ((await client.ConnectAsync()).SocketError != SocketError.Success)
                            await Task.Delay(new Random().Next(100, 200));

                        ce.Signal();
                        ce.Wait();

                        while (true)
                        {
                            homeController.DummyCall();
                            Interlocked.Increment(ref reqCount);
                        }
                    }
                }, null);
            }

            ce.Wait();

            long prev = 0;
            while(true)
            {
                Thread.Sleep(1000);
                long rCount = Interlocked.Read(ref reqCount);
                ulong reqPerSecond = unchecked((ulong)(rCount - prev));
                prev = rCount;
                Console.SetCursorPosition(0, 0);
                Console.Clear();
                Console.WriteLine($"Request per second: {reqPerSecond}");
            }
        }
    }
}
