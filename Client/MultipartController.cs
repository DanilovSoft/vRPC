using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;

namespace Client
{
    [AllowAnonymous]
    public class MultipartController : ServerController
    {
        public int TcpData(int connectionId)
        {
            return connectionId;
        }
    }
}
