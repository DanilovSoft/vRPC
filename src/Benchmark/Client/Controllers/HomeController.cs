using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using System.Diagnostics;

namespace Client.Controllers
{
    public class HomeController : RpcController
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IHostApplicationLifetime _lifetime;

        public HomeController(ILogger<HomeController> logger, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _lifetime = lifetime;
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

        public void Notify()
        {
            //Debug.WriteLine("Notify");
        }
    }
}
