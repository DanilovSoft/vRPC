﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using Microsoft.IO;

namespace Server
{
    public class Program
    {
        private const int Port = 65125;
        private static readonly RecyclableMemoryStreamManager MemoryManager = new();
        private static readonly object ConLock = new();
        private static readonly long ReqCount;
        private static long Connections;

        static async Task Main()
        {
            Console.Title = "Сервер";
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

            RpcInitializer.Initialize(MemoryManager);
            using (var listener = new VRpcListener(IPAddress.Any, Port))
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
                    e.Cancel = true;
                    lock (ConLock)
                    {
                        Console.WriteLine("Stopping...");
                    }
                    listener.BeginShutdown(TimeSpan.FromSeconds(3), "Пользователь нажал Ctrl+C");
                };

                listener.ClientConnected += Listener_ClientConnected;
                listener.ClientDisconnected += Listener_ClientDisconnected;

                //var lt = listener.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

                await listener.RunAsync();
                //listener.Start();

                //Console.Clear();
                //long prev = 0;
                //var sw = Stopwatch.StartNew();
                //while (!listener.Completion.IsCompleted)
                //{
                //    Thread.Sleep(1000);
                //    long elapsedMs = sw.ElapsedMilliseconds;
                //    long rCount = Interlocked.Read(ref ReqCount);
                //    ulong reqPerSecond = unchecked((ulong)(rCount - prev));
                //    prev = rCount;
                //    sw.Restart();

                //    var reqPerSec = (int)Math.Round(reqPerSecond * 1000d / elapsedMs);
                //    ToConsole(Interlocked.Read(ref _connections), reqPerSec, ReqCount);
                //}
                //ToConsole(0, 0, 0);
            }
        }

        private static void ToConsole(long connections, int reqPerSec, long reqCount)
        {
            lock (ConLock)
            {
                Console.SetCursorPosition(0, 0);
                Console.WriteLine($"Connections: {connections,-10}");
                Console.WriteLine($"Request per second: {reqPerSec,-10}");
                Console.WriteLine($"Requests: {reqCount,-15:g}");
            }
        }

        private static void Listener_ClientDisconnected(object sender, ClientDisconnectedEventArgs e)
        {
            Interlocked.Decrement(ref Connections);
        }

        private static void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            //var p = e.Connection.Listener.ServiceProvider.GetRequiredService<Program>();
            //var cr = e.Connection.Shutdown(TimeSpan.FromSeconds(3), "test");

            //var logger = e.Connection.ServiceProvider.GetRequiredService<ILogger<Program>>();

            Interlocked.Increment(ref Connections);

            //while (true)
            //{
            //    await e.Connection.GetProxy<IClientHomeController>().NotifyAsync();
            //    Thread.Sleep(1000);
            //}

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
