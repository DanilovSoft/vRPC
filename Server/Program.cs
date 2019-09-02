using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using vRPC;

namespace Server
{
    public class Program
    {
        private const int Port = 65125;
        private static readonly object _conLock = new object();
        private static long _connections;
        public static long ReqCount;

        static void Main()
        {
            Console.Title = "Сервер";
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            using (var listener = new Listener(IPAddress.Any, Port))
            {
                listener.ConfigureService(ioc =>
                {
                    ioc.AddLogging(loggingBuilder =>
                    {
                        loggingBuilder
                            .AddConsole()
                            .AddDebug();
                    });

                    ioc.AddSingleton(new Program());
                });

                Console.CancelKeyPress += (_, e) =>
                {
                    //var log = listener.ServiceProvider.GetRequiredService<ILogger<Program>>();
                    //log.LogWarning("Stopping...");

                    e.Cancel = true;
                    lock (_conLock)
                    {
                        Console.WriteLine("Stopping...");
                    }
                    listener.Stop(TimeSpan.FromSeconds(3), "Пользователь нажал Ctrl+C");
                };

                //listener.Configure(serviceProvider =>
                //{
                //    ILogger logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                //    logger.LogInformation("Ожидание подключений...");
                //});

                listener.ClientConnected += Listener_ClientConnected;
                listener.ClientDisconnected += Listener_ClientDisconnected;

                listener.Start();

                Console.Clear();
                long prev = 0;
                var sw = Stopwatch.StartNew();
                while (!listener.Completion.IsCompleted)
                {
                    Thread.Sleep(1000);
                    long elapsedMs = sw.ElapsedMilliseconds;
                    long rCount = Interlocked.Read(ref ReqCount);
                    ulong reqPerSecond = unchecked((ulong)(rCount - prev));
                    prev = rCount;
                    sw.Restart();

                    var reqPerSec = (int)Math.Round(reqPerSecond * 1000d / elapsedMs);

                    lock (_conLock)
                    {
                        Console.SetCursorPosition(0, 0);
                        Console.WriteLine($"Connections: {Interlocked.Read(ref _connections).ToString().PadRight(10)}");
                        Console.WriteLine($"Request per second: {reqPerSec.ToString().PadRight(10)}");
                        Console.WriteLine($"Requests: {ReqCount.ToString("g").PadRight(15)}");
                    }
                }
            }
        }

        private static void Listener_ClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            Interlocked.Decrement(ref _connections);
        }

        private static void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            //var logger = e.Connection.ServiceProvider.GetRequiredService<ILogger<Program>>();

            Interlocked.Increment(ref _connections);
            
            //var logger = e.Connection.ServiceProvider.GetRequiredService<ILogger<Program>>();

            //logger.LogInformation("Инициализируем подключенного клиента");

            //try
            //{
            //    // Назначить подключенному клиенту уникальный идентификатор.
            //    await e.Connection.GetProxy<IClientHomeController>().SetClientIdAsync(1234);
            //}
            //catch (Exception) when (!e.Connection.IsConnected)
            //{
            //    logger.LogError("Клиент отключился");
            //}
        }
    }
}
