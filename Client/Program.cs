using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using vRPC;

namespace Client
{
    class Program
    {
        private const int Port = 65125;
        private static int Threads;

        static void Main()
        {
            Console.Title = "Клиент";
            string ipStr;
            IPAddress ipAddress;
            do
            {
                Console.Write("IP адрес сервера (127.0.0.1): ");
                ipStr = Console.ReadLine();
                if (ipStr == "")
                    ipStr = "127.0.0.1";

            } while (!IPAddress.TryParse(ipStr, out ipAddress));

            string cpusStr;
            int processorCount = Environment.ProcessorCount;
            do
            {
                Console.Write($"Сколько потоков (ядер – {processorCount}): ");
                cpusStr = Console.ReadLine();
                if (cpusStr == "")
                    cpusStr = $"{processorCount}";

            } while (!int.TryParse(cpusStr, out Threads));

            long reqCount = 0;
            int activeThreads = 0;

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            for (int i = 0; i < Threads; i++)
            {
                new Thread(_ =>
                {
                    Interlocked.Increment(ref activeThreads);

                    using (var client = new vRPC.Client(ipAddress.ToString(), Port))
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
                            //while (client.ConnectAsync().GetAwaiter().GetResult() != SocketError.Success)
                            //    Thread.Sleep(new Random().Next(200, 400));

                            while (true)
                            {
                                try
                                {
                                    DateTime date = homeController.DummyCall("TestTestTestTestTestTestTestTestTestTestTestTestTestTest", 123, 123L, DateTime.Now);
                                }
                                catch (Exception ex)
                                {
                                    break;
                                }
                                Interlocked.Increment(ref reqCount);
                            }
                            Thread.Sleep(new Random().Next(200, 400));
                        } while (true);
                    }
                }).Start();
            }

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
