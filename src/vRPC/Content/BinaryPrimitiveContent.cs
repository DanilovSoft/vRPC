using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Используется Little-Endian.
    /// </summary>
    public class BinaryPrimitiveContent : VRpcContent
    {
        private readonly byte[] _binaryValue;

        public BinaryPrimitiveContent(short value)
        {
            _binaryValue = new byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(_binaryValue, value);
        }

        public BinaryPrimitiveContent(int value)
        {
            _binaryValue = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(_binaryValue, value);
        }

        public BinaryPrimitiveContent(long value)
        {
            _binaryValue = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(_binaryValue, value);
        }

        public BinaryPrimitiveContent(ushort value)
        {
            _binaryValue = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(_binaryValue, value);
        }

        public BinaryPrimitiveContent(uint value)
        {
            _binaryValue = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(_binaryValue, value);
        }

        public BinaryPrimitiveContent(ulong value)
        {
            _binaryValue = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(_binaryValue, value);
        }

        protected internal override bool TryComputeLength(out int length)
        {
            length = _binaryValue.Length;
            return true;
        }

        private protected override Multipart SerializeToStream(IBufferWriter<byte> writer)
        {
            writer.Write(_binaryValue);
            throw new NotImplementedException();
        }
    }
}
