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
    internal class BenchmarkController : ServerController
    {
        public void TestNotification()
        {
            
        }
        
        [TcpNoDelay]
        public int VoidOneArg(int n)
        {
            throw new VRpcBadRequestException("проверка");
            return 123;
        }
    }
}
