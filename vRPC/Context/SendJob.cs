using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace vRPC
{
    ///// <summary>
    ///// Представляет сериализованный, не фрагментированный запрос или ответ на запрос.
    ///// </summary>
    //[DebuggerDisplay(@"\{Size = {MessageStream.Length} bytes\}")]
    //internal readonly struct SendJob : IDisposable // Структура должна быть меньше 16 байт иначе class.
    //{
    //    public MemoryPoolStream MessageStream { get; }
    //    public MessageType MessageType { get; }
    //    //public HeaderDto Header { get; }
    //    public int HeaderSize { get; }

    //    [DebuggerStepThrough]
    //    public SendJob(SerializedMessage serializedMessage)
    //    {
    //        //Header = header;
    //        //HeaderSize = headerSize;
    //        //MessageStream = messageStream;
    //        //MessageType = messageType;

    //        Debug.Assert(Marshal.SizeOf(this) < 16, "Структура превышает разумный размер.");
    //    }

    //    public void Dispose()
    //    {
    //        MessageStream.Dispose();
    //    }
    //}
}
