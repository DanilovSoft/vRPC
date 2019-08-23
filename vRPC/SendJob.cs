using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace vRPC
{
    /// <summary>
    /// Представляет сериализованный, не фрагментированный запрос или ответ на запрос.
    /// </summary>
    internal readonly struct SendJob
    {
        public MemoryPoolStream ContentStream { get; }
        public MessageType MessageType { get; }
        public Header Header { get; }
        public int HeaderSize { get; }

        [DebuggerStepThrough]
        public SendJob(Header header, int headerSize, MemoryPoolStream contentStream, MessageType messageType)
        {
            Header = header;
            HeaderSize = headerSize;
            ContentStream = contentStream;
            MessageType = messageType;
        }
    }
}
