using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using System.Diagnostics;

namespace Server.Controllers
{
    [AllowAnonymous]
    public class HomeController : ServerController
    {
        private readonly ILogger _logger;
        private readonly Program _program;

        public HomeController(ILogger<HomeController> logger, Program program)
        {
            _logger = logger;
            _program = program;
        }

        [ProducesProtoBuf]
        public DateTime DummyCall(string s)
        {
            Interlocked.Increment(ref Program.ReqCount);
            return DateTime.Now;
        }

        public DateTime Test(TestDto test)
        {
            return DateTime.Now;
        }
    }
}
