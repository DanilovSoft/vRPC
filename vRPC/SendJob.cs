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
        public SocketWrapper SocketQueue { get; }
        public MemoryPoolStream MemoryPoolStream { get; }
        public MessageType MessageType { get; }

        [DebuggerStepThrough]
        public SendJob(SocketWrapper socketQueue, MemoryPoolStream memoryPoolStream, MessageType messageType)
        {
            SocketQueue = socketQueue;
            MemoryPoolStream = memoryPoolStream;
            MessageType = messageType;
        }
    }
}
