using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC;

namespace Client
{
    class Program
    {
        private const int Port = 65125;
        private static readonly object ConLock = new();
        private static bool AppExit;
        private static int Threads;

        static void Main()
        {
            Console.Title = "Клиент";
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            string ipStr;
            IPAddress ipAddress;
            do
            {
                Console.Write("IP адрес сервера [127.0.0.1]: ");
                ipStr = Console.ReadLine();
                if (ipStr == "")
                    ipStr = "127.0.0.1";

            } while (!IPAddress.TryParse(ipStr, out ipAddress));

            string cpusStr;
            var processorCount = Environment.ProcessorCount;
            do
            {
                Console.Write($"Ядер – {processorCount}. Сколько потоков [1]: ");
                cpusStr = Console.ReadLine();
                if (cpusStr == "")
                    cpusStr = $"{1}";

            } while (!int.TryParse(cpusStr, out Threads));

            long reqCount = 0;
            var activeThreads = 0;

            var threads = new List<Task>(Threads);
            for (var i = 0; i < Threads; i++)
            {
                var t = Task.Run(async () =>
                {
                    if (AppExit)
                        return;

                    Interlocked.Increment(ref activeThreads);

                    using (var client = new VRpcClient(new Uri($"ws://{ipAddress}:{Port}"), true))
                    {
                        Console.CancelKeyPress += (__, e) => Console_CancelKeyPress(e, client);

                        client.ConfigureService(ioc =>
                        {
                            ioc.AddLogging(loggingBuilder =>
                            {
                                loggingBuilder
                                    .AddConsole();
                            });
                        });

                        var controller = client.GetProxy<IBenchmarkController>();
                        
                        while (true)
                        {
                            ConnectResult conResult;
                            while ((conResult = client.ConnectEx()).State == ConnectionState.SocketError)
                            {
                                Thread.Sleep(new Random().Next(2000, 3000));
                            }

                            if (conResult.State == ConnectionState.ShutdownRequest)
                                break;

                            while (true)
                            {
                                try
                                {
                                    await controller.VoidNoArgsAsync();
                                }
                                catch (VRpcShutdownException)
                                {
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine(ex);
                                    Thread.Sleep(new Random().Next(2000, 3000));
                                    break;
                                }
                                Interlocked.Increment(ref reqCount);
                            }
                        }
                        // Подождать грациозное закрытие.
                        var closeReason = client.WaitCompletion();
                    }
                    Interlocked.Decrement(ref activeThreads);
                });
                
                threads.Add(t);
            }

            long prev = 0;
            Console.Clear();
            var sw = Stopwatch.StartNew();
            while (threads.TrueForAll(x => x.Status != TaskStatus.RanToCompletion))
            {
                Thread.Sleep(1000);
                var elapsedMs = sw.ElapsedMilliseconds;
                var rCount = Interlocked.Read(ref reqCount);
                var reqPerSecond = unchecked((ulong)(rCount - prev));
                prev = rCount;
                sw.Restart();

                var reqPerSec = (int)Math.Round(reqPerSecond * 1000d / elapsedMs);

                PrintConsole(activeThreads, reqPerSec);
            }
            PrintConsole(0, 0);
        }

        private static void PrintConsole(int activeThreads, int reqPerSec)
        {
            lock (ConLock)
            {
                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"Active Threads: {activeThreads,-10}");
                Console.WriteLine($"Request per second: {reqPerSec,-10}");
            }
        }

        private static void Console_CancelKeyPress(ConsoleCancelEventArgs e, VRpcClient client)
        {
            AppExit = true;

            if (!e.Cancel)
            {
                e.Cancel = true;
                lock (ConLock)
                {
                    Console.WriteLine("Stopping...");
                }
            }
            client.Shutdown(TimeSpan.FromSeconds(100), "Был нажат Ctrl+C");
        }
    }
}
