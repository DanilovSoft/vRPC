using ProtoBuf;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Сериализуемое сообщение для передачи через сокет. 
    /// На данный момент сериализуется только в Json.
    /// </summary>
    [DebuggerDisplay(@"\{Request = {ActionName,nq}\}")]
    internal sealed class RequestMessageDto
    {
        /// <summary>
        /// Вызываемый метод.
        /// </summary>
        [JsonPropertyName("n")]
        public string ActionName { get; }

        /// <summary>
        /// Аргументы вызываемого метода.
        /// </summary>
        [JsonPropertyName("a")]
        public object[] Args { get; }

        public RequestMessageDto(string actionName, object[] args)
        {
            ActionName = actionName;
            Args = args;
        }
    }
}