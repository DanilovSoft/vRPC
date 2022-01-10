using DanilovSoft.vRPC;

namespace AspNetCore.RpcControllers
{
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
