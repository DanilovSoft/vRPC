namespace AspNetCore.RpcControllers
{
    using DanilovSoft.vRPC;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    [AllowAnonymous]
    public class TestController : RpcController
    {
        public TestController()
        {
            
        }

        public int GetSum(int x, int y)
        {
            return 2;
        }
    }
}
