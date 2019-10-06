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
    [DebuggerDisplay(@"\{{TargetMethod.GetControllerActionName()}\}")]
    internal sealed class ControllerAction
    {
        public Action<Stream, object> Serializer { get; }
        public MethodInfo TargetMethod { get; }
        /// <summary>
        /// Формат возвращаемых данных.
        /// </summary>
        public string ProducesEncoding { get; }
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly Func<object, object[], object> FastInvokeDelegate;
        /// <summary>
        /// Контроллер для активации через IoC.
        /// </summary>
        public Type ControllerType { get; }

        public ControllerAction(Type controllerType, MethodInfo methodInfo)
        {
            ControllerType = controllerType;
            TargetMethod = methodInfo;
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
