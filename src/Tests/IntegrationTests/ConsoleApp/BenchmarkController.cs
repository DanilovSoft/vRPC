using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;
using Microsoft.IO;

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
