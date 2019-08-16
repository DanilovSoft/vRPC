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
    class Program
    {
        private const int Port = 65125;

        static async Task Main()
        {
            Console.Title = "Сервер";

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
                });

                listener.Configure(serviceProvider =>
                {
                    ILogger logger = serviceProvider.GetRequiredService<ILogger<Program>>();
                    logger.LogInformation("Ожидание подключений...");
                });

                listener.ClientConnected += Listener_Connected;

                await listener.RunAsync();
            }
        }

        private static async void Listener_Connected(object sender, ClientConnectedEventArgs e)
        {
            var logger = e.Connection.ServiceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Инициализируем подключенного клиента");

            try
            {
                // Назначить подключенному клиенту уникальный идентификатор.
                await e.Connection.GetProxy<IClientHomeController>().SetClientIdAsync(1234);
            }
            catch (Exception) when (!e.Connection.IsConnected)
            {
                logger.LogError("Клиент отключился");
            }
        }
    }
}
