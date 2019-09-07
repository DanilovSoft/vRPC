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
        [JsonProperty("n", Order = 1)]
        public string ActionName { get; }

        /// <summary>
        /// Аргументы вызываемого метода.
        /// </summary>
        [JsonProperty("a", Order = 2)]
        public JToken[] Args { get; }

        public RequestMessageDto(string actionName, JToken[] args)
        {
            ActionName = actionName;
            Args = args;
        }
    }
}