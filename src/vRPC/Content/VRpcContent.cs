using System;
using System.Buffers;
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

        private protected abstract Multipart SerializeToStream(IBufferWriter<byte> writer);
        //protected internal abstract bool TrySerializeSynchronously(Stream stream);

        [DebuggerStepThrough]
        internal Multipart InnerSerializeToStream(IBufferWriter<byte> writer) => SerializeToStream(writer);

        internal abstract ReadOnlyMemory<byte> GetMemory();

        private protected static void SerializeHeader(IBufferWriter<byte> writer, int contentSize, string contentEncoding)
        {
            throw new NotImplementedException();
            //using (var stream = new ReadOnlyMemoryStream(writer.GetMemory()))
            //{
            //    Serializer.NonGeneric.SerializeWithLengthPrefix(stream, header, PrefixStyle.Base128, 1);
            //}
        }

        private void ReadPrefixBase128(Memory<byte> buffer)
        {
//            if (buffer.Span[0] < 128)
//            {
//                return buffer.Span[0];
//            }
//            else if (buffer.Span[0] < 192)
//            {
//                //await _stream.ReadBlockAsync(buffer.Slice(1, 1)).ConfigureAwait(false);

//                int v = 0;
//                for (int i = 0; i < 2; i++)
//                    v = (v << 8) + buffer.Span[i];

//                return v ^ 0x8000;
//            }
//            else if (buffer.Span[0] < 224)
//            {
//                //await _stream.ReadBlockAsync(buffer.Slice(1, 2)).ConfigureAwait(false);

//                int v = 0;
//                for (int i = 0; i < 3; i++)
//                    v = (v << 8) + buffer.Span[i];

//                return v ^ 0xC00000;
//            }
//            else if (buffer.Span[0] < 240)
//            {
//                //await _stream.ReadBlockAsync(buffer.Slice(1, 3)).ConfigureAwait(false);

//                int v = 0;
//                for (int i = 0; i < 4; i++)
//                    v = (v << 8) + buffer.Span[i];

//                return (int)(v ^ 0xE0000000);
//            }
//            else if (buffer.Span[0] == 240)
//            {
//                //await _stream.ReadBlockAsync(buffer.Slice(0, 4)).ConfigureAwait(false);

//                int v = 0;
//                for (int i = 0; i < 4; i++)
//                    v = (v << 8) + buffer.Span[i];

//                return v;
//            }
//            else
//            {
//#if DEBUG
//                if (Debugger.IsAttached)
//                    Debugger.Break();

//#endif
//                // Не должно быть такого.
//                throw new MikroTikUnknownLengthException();
//            }
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
