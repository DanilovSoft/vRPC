using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Runtime.Serialization;

namespace vRPC
{
    /// <summary>
    /// Сериализуемое сообщение для передачи через сокет.
    /// </summary>
    [ProtoContract]
    internal class RequestMessage
    {
        /// <summary>
        /// Вызываемый метод.
        /// </summary>
        [ProtoMember(1)]
        [JsonProperty("n")]
        public string ActionName { get; set; }

        /// <summary>
        /// Аргументы вызываемого метода.
        /// </summary>
        [ProtoMember(2)]
        [JsonProperty("a")]
        public JToken[] Args { get; set; }

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