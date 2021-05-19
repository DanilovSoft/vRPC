namespace AspNetCoreTest.Controllers
{
	using EdjCase.JsonRpc.Router;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

	public class TestController : RpcController
	{
		public int GetSum(int x, int y)
		{
			return x + y;
		}
	}
}
