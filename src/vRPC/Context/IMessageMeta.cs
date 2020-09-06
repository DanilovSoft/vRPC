
using System;

namespace DanilovSoft.vRPC
{
    [Obsolete]
    internal interface IMessageMeta
    {
        /// <summary>
        /// True если сообщение является запросом, иначе 
        /// сообщение это результат запроса.
        /// </summary>
        bool IsRequest { get; }
        /// <summary>
        /// Может быть True когда IsRequest тоже является True.
        /// </summary>
        bool IsNotificationRequest { get; }
        bool TcpNoDelay { get; }
        bool IsJsonRpc { get; }
    }
}
