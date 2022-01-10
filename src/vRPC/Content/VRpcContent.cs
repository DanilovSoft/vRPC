using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
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

        private protected abstract Multipart SerializeToStream(IBufferWriter<byte> writer);
        //protected internal abstract bool TrySerializeSynchronously(Stream stream);

        [DebuggerStepThrough]
        internal Multipart InnerSerializeToStream(IBufferWriter<byte> writer) => SerializeToStream(writer);

        private protected static void SerializeHeader(IBufferWriter<byte> writer, in MultipartHeaderDto header)
        {
            using (var stream = new ReadOnlyMemoryStream(writer.GetMemory()))
            {
                Serializer.NonGeneric.SerializeWithLengthPrefix(stream, header, PrefixStyle.Base128, 1);
            }
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
