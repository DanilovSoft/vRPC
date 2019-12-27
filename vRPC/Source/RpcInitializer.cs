using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public static class RpcInitializer
    {
        public static void Initialize(RecyclableMemoryStreamManager memoryManager)
        {
            GlobalVars.Initialize(memoryManager);
        }
    }
}
