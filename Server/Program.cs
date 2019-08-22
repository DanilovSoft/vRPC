﻿using Microsoft.Extensions.Configuration;
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
        public static long ReqCount;

        static void Main()
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

                long prev = 0;
                while (true)
                {
                    Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                    Thread.Sleep(1000);
                    long c = Interlocked.Read(ref _connections);
                    long rCount = Interlocked.Read(ref ReqCount);
                    ulong reqPerSecond = unchecked((ulong)(rCount - prev));
                    prev = rCount;
                    Console.SetCursorPosition(0, 0);
                    Console.WriteLine($"Connections: {c.ToString().PadRight(10, ' ')}");
                    Console.WriteLine($"Request per second: {reqPerSecond.ToString().PadRight(10, ' ')}");
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
