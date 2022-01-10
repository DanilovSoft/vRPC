using Microsoft.IO;

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
