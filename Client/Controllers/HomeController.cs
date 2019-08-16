using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using vRPC;

namespace Client.Controllers
{
    public class HomeController : ClientController
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public async Task<string> SayHello(string s)
        {
            Console.WriteLine($"Сервер сказал: {s}");
            Console.Write("Введите ответ для сервера: ");
            string line = await Console.In.ReadLineAsync();
            return line;
        }

        public void SetClientId(int id)
        {
            Console.WriteLine($"Сервер прислал уникальный идентификатор: {id}");
        }
    }
}
