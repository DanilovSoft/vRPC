using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC;

namespace Server
{
    [AllowAnonymous]
    public class MultipartController : ServerController
    {
        public ValueTask Test()
        {
            return default;
        }
    }
}
