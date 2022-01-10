using EdjCase.JsonRpc.Router;

namespace AspNetCoreTest.Controllers
{
    public class TestController : RpcController
	{
		public int GetSum(int x, int y)
		{
			return x + y;
		}
	}
}
