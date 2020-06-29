using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.DTO;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    public class MemoryContent : VRpcContent
    {
        public ReadOnlyMemory<byte> Content { get; }

        public MemoryContent(ReadOnlyMemory<byte> content)
        {
            Content = content;
        }

        protected internal sealed override bool TryComputeLength(out int length)
        {
            length = Content.Length;
            return true;
        }

        private protected sealed override Multipart SerializeToStream(IBufferWriter<byte> writer)
        {
            throw new NotImplementedException();
            //int contentLength = (int)stream.Position;
            //CopyMemory(stream, Memory);

            //int headerPosition = (int)stream.Position;

            //contentLength = headerPosition - contentLength;

            //SerializeHeader(stream, new MultipartHeaderDto(contentLength, KnownEncoding.RawEncoding));
            
            //byte headerSize = (byte)((int)stream.Position - headerPosition);

            //return new Multipart(contentLength, headerSize);
        }

#if NETSTANDARD2_0 || NET472
        private static void CopyMemory(Stream stream, ReadOnlyMemory<byte> memory)
        {
            if (MemoryMarshal.TryGetArray(memory, out var segment))
            {
                stream.Write(segment.Array, segment.Offset, segment.Count);
            }
            else
            {
                ManualCopySpan(stream, memory.Span);
            }
        }

        private static void ManualCopySpan(Stream stream, ReadOnlySpan<byte> buffer)
        {
            byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                buffer.CopyTo(sharedBuffer);
                stream.Write(sharedBuffer, 0, buffer.Length);
            }
            finally { ArrayPool<byte>.Shared.Return(sharedBuffer); }
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CopyMemory(Stream stream, ReadOnlyMemory<byte> memory)
        {
            stream.Write(memory.Span);
        }
#endif

        internal override ReadOnlyMemory<byte> GetMemory()
        {
            return Content;
        }
    }
}
