using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит исчерпывающую информацию о методе контроллера.
    /// </summary>
    [DebuggerDisplay(@"\{{_methodFullName,nq}\}")]
    internal sealed class ControllerAction
    {
        public Action<Stream, object> Serializer { get; }
        private readonly string _methodFullName;
        public MethodInfo TargetMethod { get; }
        /// <summary>
        /// Формат возвращаемых данных.
        /// </summary>
        public string ProducesEncoding { get; }
        public readonly Func<object, object[], object> FastInvokeDelegate;

        public ControllerAction(MethodInfo methodInfo, string methodFullName)
        {
            TargetMethod = methodInfo;
            _methodFullName = methodFullName;
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

            FastInvokeDelegate = DynamicMethodFactory.CreateMethodCall(methodInfo, skipConvertion: true);
        }
    }
}
