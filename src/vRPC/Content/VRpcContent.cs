using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.DTO;
using DanilovSoft.vRPC.Source;
using ProtoBuf;

namespace DanilovSoft.vRPC.Content
{
    public abstract class VRpcContent : IDisposable
    {
        // Derived types return true if they're able to compute the length. It's OK if derived types return false to
        // indicate that they're not able to compute the length. The transport channel needs to decide what to do in
        // that case (send chunked, buffer first, etc.).
        protected internal abstract bool TryComputeLength(out int length);

        private protected abstract Multipart SerializeToStream(Stream stream);
        //protected internal abstract bool TrySerializeSynchronously(Stream stream);

        [DebuggerStepThrough]
        internal Multipart InnerSerializeToStream(Stream stream) => SerializeToStream(stream);

        private protected static void SerializeHeader(Stream stream, in MultipartHeaderDto header)
        {
            Serializer.NonGeneric.SerializeWithLengthPrefix(stream, header, PrefixStyle.Base128, 1);
        }

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
