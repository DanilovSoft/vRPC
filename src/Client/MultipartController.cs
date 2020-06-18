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
    internal class MyServerController : ServerController
    {
        [ProducesProtoBuf]
        public TestStruct Test2()
        {
            return new TestStruct(123);
        }
    }
}
