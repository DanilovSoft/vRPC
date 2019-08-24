using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace vRPC
{
    /// <summary>
    /// Сериализуемый аргумент вызываемого метода.
    /// </summary>
    [DataContract]
    [DebuggerDisplay(@"\{{ParameterName}: {Value}\}")]
    internal sealed class ArgDto
    {
        [JsonProperty("n")]
        public string ParameterName { get; set; }

        [JsonProperty("v")]
        public JToken Value { get; set; }

        [JsonConstructor]
        private ArgDto() { }

        public ArgDto(string parameterName, object value)
        {
            ParameterName = parameterName;
            Value = value == null ? null : JToken.FromObject(value);
        }
    }
}
