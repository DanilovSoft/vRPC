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
    [DebuggerDisplay(@"\{{_fullPath}\}")]
    internal sealed class ControllerAction
    {
        public Action<Stream, object> Serializer { get; }
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
                Serializer = ExtensionMethods.SerializeObjectProtobuf;
                ProducesEncoding = ProducesProtoBufAttribute.Encoding;
            }
            else
            {
                // Сериализатор по умолчанию — Json.
                Serializer = ExtensionMethods.SerializeObjectJson;
                ProducesEncoding = "json";
            }
        }

        public void SerializeObject(Stream destination, object instance)
        {
            Serializer.Invoke(destination, instance);
        }
    }
}
