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
        public int HeaderSizeWithPrefix { get; }

        [DebuggerStepThrough]
        public SendJob(Header header, int headerSizeWithPrefix, MemoryPoolStream contentStream, MessageType messageType)
        {
            Header = header;
            HeaderSizeWithPrefix = headerSizeWithPrefix;
            ContentStream = contentStream;
            MessageType = messageType;
        }
    }
}
