using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    // Тип передаваемого сообщения. Не сериализуется.
    internal enum MessageType
    {
        Request,
        Response,
    }
}
