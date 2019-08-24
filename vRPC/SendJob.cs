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
    [DebuggerDisplay(@"\{Size = {MessageStream.Length} bytes\}")]
    internal readonly struct SendJob
    {
        public MemoryPoolStream MessageStream { get; }
        public MessageType MessageType { get; }
        public HeaderDto Header { get; }
        public int HeaderSize { get; }

        [DebuggerStepThrough]
        public SendJob(HeaderDto header, int headerSize, MemoryPoolStream messageStream, MessageType messageType)
        {
            Header = header;
            HeaderSize = headerSize;
            MessageStream = messageStream;
            MessageType = messageType;
        }
    }
}
