using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Content
{
    public abstract class VRpcContent : IDisposable
    {
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            
        }
    }
}
