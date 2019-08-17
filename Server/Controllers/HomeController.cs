using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using vRPC;

namespace Server.Controllers
{
    [AllowAnonymous]
    public class HomeController : ServerController
    {
        private readonly ILogger _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public async Task<string> Echo()
        {
            _logger.LogInformation("Отправляем 'hello client'");
            string resp = await Context.GetProxy<IClientHomeController>().SayHelloAsync("hello client");
            _logger.LogInformation($"Клиент ответил: {resp}");
            return resp;
        }
            

        public IActionResult Test0()
        {
            return Ok(123);
        }

        public async Task Test1()
        {
            await Task.Delay(1000);
        }

        [ProducesProtoBuf]
        public async Task<int> Test2()
        {
            await Task.Delay(1000);
            return 123;
        }

        public async ValueTask Test3()
        {
            await Task.Delay(1000);
        }

        public async ValueTask<int> Test4()
        {
            await Task.Delay(1000);
            return 123;
        }
    }
}
