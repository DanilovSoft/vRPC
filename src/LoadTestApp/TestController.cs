using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;

namespace LoadTestApp
{
    [AllowAnonymous]
    public class TestController : RpcController
    {
        public string Ping(string msg)
        {
            return msg;
        }
    }
}
