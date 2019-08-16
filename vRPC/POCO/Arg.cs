using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace vRPC
{
    [DataContract]
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal sealed class Arg
    {
        #region Debug

        [JsonIgnore]
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => $"\"{ParameterName}\": {Value}";

        #endregion

        [JsonProperty]
        public string ParameterName;

        [JsonProperty]
        public JToken Value;

        [JsonConstructor]
        private Arg() { }

        public Arg(string parameterName, object value)
        {
            ParameterName = parameterName;
            Value = value == null ? null : JToken.FromObject(value);
        }
    }
}
