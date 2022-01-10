using DanilovSoft.vRPC;

namespace ConsoleApp
{
    [AllowAnonymous]
    internal class BenchmarkController : RpcController
    {
        public void TestNotification()
        {

        }

        public void Data(int connectionId, byte[] data)
        {

        }

        [TcpNoDelay]
        public int VoidOneArg(int n)
        {
            throw new VRpcInvalidParamsException("проверка");
            return 123;
        }
    }
}
