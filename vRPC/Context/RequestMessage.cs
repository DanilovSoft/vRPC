using System.Runtime.Serialization;

namespace vRPC
{
    /// <summary>
    /// Сериализуемое сообщение для передачи через сокет.
    /// </summary>
    internal class RequestMessage
    {
        /// <summary>
        /// Вызываемый метод.
        /// </summary>
        public string ActionName { get; set; }

        /// <summary>
        /// Аргументв вызываемого метода.
        /// </summary>
        public Arg[] Args { get; set; }

        /// <summary>
        /// Связанный заголовок этого запроса.
        /// </summary>
        [IgnoreDataMember]
        public Header Header { get; set; }

        /// <summary>
        /// Контекст связанный с текущим запросом.
        /// </summary>
        [IgnoreDataMember]
        public RequestContext RequestContext { get; set; }
    }
}