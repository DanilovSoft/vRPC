using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using System.Diagnostics;
using DanilovSoft.vRPC.Decorator;

namespace Server.Controllers
{
    [AllowAnonymous]
    internal class HomeController : ServerController
    {
        private readonly ILogger _logger;
        private readonly Program _program;
        private readonly IClientHomeController _proxy;

        public HomeController(ILogger<HomeController> logger, Program program, IProxy<IClientHomeController> proxy)
        {
            _logger = logger;
            _program = program;
            _proxy = proxy.Proxy;
        }

        [ProducesProtoBuf]
        public void DummyCall(int n)
        {
            Interlocked.Increment(ref Program.ReqCount);
        }

        public async void Test()
        {
            var proxy = Context.GetProxy<ITest>(out ServerInterfaceProxy decorator);
            proxy.Bla();
        }

        private async void PrivateTest()
        {
            try
            {
                var cr = await Context.ShutdownAsync(TimeSpan.FromSeconds(100), "test");
            }
            catch (Exception ex)
            {

                throw;
            }
        }
    }

    public interface Controller
    {
        void Bla();
    }

    public interface I
    {
        void Bla();
    }

    public interface ITest
    {
        void Bla();
    }
}
