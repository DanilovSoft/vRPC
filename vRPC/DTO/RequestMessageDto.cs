using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace vRPC
{
    /// <summary>
    /// Сериализуемое сообщение для передачи через сокет. На данный момент сериализуется только в Json.
    /// </summary>
    [DebuggerDisplay(@"\{Request = {ActionName,nq}\}")]
    internal sealed class RequestMessageDto
    {
        /// <summary>
        /// Вызываемый метод.
        /// </summary>
        [JsonProperty("n")]
        public string ActionName { get; set; }

        /// <summary>
        /// Аргументы вызываемого метода.
        /// </summary>
        [JsonProperty("a")]
        public JToken[] Args { get; set; }
    }
}