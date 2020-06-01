using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC.Content
{
    public abstract class VRpcContent : IDisposable
    {
        // Derived types return true if they're able to compute the length. It's OK if derived types return false to
        // indicate that they're not able to compute the length. The transport channel needs to decide what to do in
        // that case (send chunked, buffer first, etc.).
        protected internal abstract bool TryComputeLength(out long length);

        protected internal abstract Task SerializeToStreamAsync(Stream stream);

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
