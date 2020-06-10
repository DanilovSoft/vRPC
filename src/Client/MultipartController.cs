using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;
using Microsoft.IO;

namespace Client
{
    [AllowAnonymous]
    internal class MultipartController : ServerController
    {
        //public async ValueTask<int> TcpData(int connectionId, RentedMemory memory)
        //{
        //    await Task.Delay(5000);

        //    throw new InvalidOperationException("test");

        //    memory.Dispose();
        //    //return connectionId;
        //}

        public void TcpData(int id, byte[] data)
        {
            
        }

        public int Test2()
        {
            return 1;
        }
    }
}
