using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace vRPC
{
    /// <summary>
    /// Содержит исчерпывающую информацию о методе контроллера.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal sealed class ControllerAction
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"\"{_fullPath}\"" + "}";
        private readonly Action<Stream, object> _serializer;
        private readonly string _fullPath;
        public MethodInfo TargetMethod { get; }
        /// <summary>
        /// Формат возвращаемых данных.
        /// </summary>
        public string ProducesEncoding { get; }

        public ControllerAction(MethodInfo methodInfo, string fullPath)
        {
            TargetMethod = methodInfo;
            _fullPath = fullPath;
            var protobufAttrib = methodInfo.GetCustomAttribute<ProducesProtoBufAttribute>();
            if (protobufAttrib != null)
            {
                _serializer = ExtensionMethods.SerializeObjectProtobuf;
                ProducesEncoding = ProducesProtoBufAttribute.Encoding;
            }
            else
            {
                // Сериализатор по умолчанию — Json.
                _serializer = ExtensionMethods.SerializeObjectJson;
                ProducesEncoding = "json";
            }
        }

        public void SerializeObject(Stream destination, object instance)
        {
            _serializer(destination, instance);
        }
    }
}
