using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
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
        public int ReqCount;

        static async Task Main()
        {
            Console.Title = "Сервер";

            //ThreadPool.GetAvailableThreads(out int workerThreads, out int completionPortThreads);
            //ThreadPool.GetMaxThreads(out workerThreads, out completionPortThreads);
            //ThreadPool.SetMinThreads(workerThreads, 5000);
            //ThreadPool.GetAvailableThreads(out workerThreads, out completionPortThreads);

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
                while(true)
                {
                    long c = Interlocked.Read(ref _connections);
                    Console.SetCursorPosition(0, 0);
                    Console.Write($"Connections: {c.ToString().PadRight(10, ' ')}");
                    await Task.Delay(500);
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
