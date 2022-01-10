using DanilovSoft.vRPC;

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
