using DanilovSoft.vRPC;
using System;

namespace InternalConsoleApp
{
    [AllowAnonymous]
    public class TestController : RpcController
    {
        public void Message(string msg)
        {
            Console.WriteLine(msg);
        }

        public int GetSum(int x, int y)
        {
            return x + y;
        }
    }
}
