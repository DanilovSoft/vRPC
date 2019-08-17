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

        static async Task Main()
        {
            Console.Title = "Клиент";

            using (var client = new ServerContext("127.0.0.1", Port))
            {
                client.ConfigureService(ioc => 
                {
                    ioc.AddLogging(loggingBuilder =>
                    {
                        loggingBuilder
                            .AddConsole();
                    });
                });

                while((await client.ConnectAsync()).SocketError != SocketError.Success)
                    await Task.Delay(1000);

                var homeController = client.GetProxy<IHomeController>();

                while (true)
                {
                    string echo = await homeController.EchoAsync();
                    Console.WriteLine($"Сервер вернул результат: {echo}");
                }
            }
        }
    }
}
