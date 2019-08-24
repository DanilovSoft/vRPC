using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using vRPC;

namespace Client
{
    class Program
    {
        private const int Port = 65125;
        private const int Threads = 6;

        static void Main()
        {
            Console.Title = "Клиент";
            long reqCount = 0;
            int activeThreads = 0;

            ThreadPool.GetAvailableThreads(out int workerThreads, out _);
            ThreadPool.SetMinThreads(Threads, 1000);

            ThreadPool.UnsafeQueueUserWorkItem(delegate 
            {
                for (int i = 0; i < Threads; i++)
                {
                    Task.Factory.StartNew(delegate 
                    {
                        Interlocked.Increment(ref activeThreads);

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
                            var homeController = client.GetProxy<IServerHomeController>();

                            // Лучше подключиться предварительно.
                            do
                            {
                                while ((client.ConnectAsync().GetAwaiter().GetResult()).SocketError != SocketError.Success)
                                    Thread.Sleep(new Random().Next(200, 400));

                                while (true)
                                {
                                    try
                                    {
                                        DateTime date = homeController.DummyCall("Test");
                                    }
                                    catch (Exception ex)
                                    {
                                        Thread.Sleep(new Random().Next(200, 400));
                                        break;
                                    }
                                    Interlocked.Increment(ref reqCount);
                                }
                            } while (true);
                        }
                        Interlocked.Decrement(ref activeThreads);
                    }, TaskCreationOptions.LongRunning);
                }
            }, null);

            long prev = 0;
            Console.Clear();
            var sw = Stopwatch.StartNew();
            while (true)
            {
                Thread.Sleep(1000);
                long elapsedMs = sw.ElapsedMilliseconds;
                long rCount = Interlocked.Read(ref reqCount);
                ulong reqPerSecond = unchecked((ulong)(rCount - prev));
                prev = rCount;
                sw.Restart();

                var reqPerSec = (int)Math.Round(reqPerSecond * 1000d / elapsedMs);

                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"Active Threads: {activeThreads.ToString().PadRight(10, ' ')}");
                Console.WriteLine($"Request per second: {reqPerSec.ToString().PadRight(10, ' ')}");
            }
        }
    }
}
