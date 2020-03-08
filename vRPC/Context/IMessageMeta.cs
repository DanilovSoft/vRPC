
namespace DanilovSoft.vRPC
{
    internal interface IMessageMeta
    {
        /// <summary>
        /// True если сообщение является запросом, иначе 
        /// сообщение это результат запроса.
        /// </summary>
        bool IsRequest { get; }
        /// <summary>
        /// Может быть True если IsRequest тоже является True.
        /// </summary>
        bool IsNotificationRequest { get; }
    }
}
