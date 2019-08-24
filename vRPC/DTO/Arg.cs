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
    [ProtoContract]
    [DebuggerDisplay(@"\{{ParameterName}: {Value}\}")]
    internal sealed class Arg
    {
        [JsonProperty("n")]
        [ProtoMember(1)]
        public string ParameterName { get; set; }

        [JsonProperty("v")]
        public JToken Value { get; set; }

        //[JsonIgnore]
        //[ProtoMember(2, DynamicType = true)]
        //public object Value2 { get; set; }

        //[JsonConstructor]
        //private Arg() { }
        public Arg() { }

        public Arg(string parameterName, object value)
        {
            ParameterName = parameterName;
            Value = value == null ? null : JToken.FromObject(value);
            //Value2 = value;
        }
    }
}
