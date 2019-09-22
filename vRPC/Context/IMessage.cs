
namespace DanilovSoft.vRPC
{
    internal interface IMessage
    {
        /// <summary>
        /// True если сообщение является запросом, иначе 
        /// сообщение это результат запроса.
        /// </summary>
        bool IsRequest { get; }
    }
}
