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
        private static long _connections;
        public static long ReqCount;

        static void Main()
        {
            Console.Title = "Сервер";
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

            using (var listener = new Listener(IPAddress.Any, Port))
            {
                listener.ConfigureService(ioc =>
                {
                    ioc.AddLogging(loggingBuilder =>
                    {
                        //loggingBuilder
                        //    .AddConsole()
                        //    .AddDebug();
                    });

                    ioc.AddSingleton(new Program());
                });

                listener.Configure(serviceProvider =>
                {
                    ILogger logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Ожидание подключений...");
                });

                listener.ClientConnected += Listener_Connected;
                listener.ClientDisconnected += Listener_ClientDisconnected;

                listener.Start();
                //await listener.RunAsync();

                long prev = 0;
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    Thread.Sleep(1000);
                    long elapsedMs = sw.ElapsedMilliseconds;
                    long rCount = Interlocked.Read(ref ReqCount);
                    ulong reqPerSecond = unchecked((ulong)(rCount - prev));
                    prev = rCount;
                    sw.Restart();

                    var reqPerSec = (int)Math.Round(reqPerSecond * 1000d / elapsedMs);

                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine($"Connections: {Interlocked.Read(ref _connections).ToString().PadRight(10)}");
                    Console.WriteLine($"Request per second: {reqPerSec.ToString().PadRight(10)}");
                    Console.WriteLine($"Requests: {ReqCount.ToString("g").PadRight(15)}");
                }
            }
        }

        private static void Listener_ClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            Interlocked.Decrement(ref _connections);
        }

        private static void Listener_Connected(object sender, ClientConnectedEventArgs e)
        {
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
