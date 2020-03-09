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
    /// Этот клас переиспользуется.
    /// </summary>
    [DebuggerDisplay(@"\{{TargetMethod.GetControllerActionName()}\}")]
    internal sealed class ControllerActionMeta
    {
        public Action<Stream, object> Serializer { get; }
        public MethodInfo TargetMethod { get; }
        /// <summary>
        /// Формат возвращаемых данных.
        /// </summary>
        public string ProducesEncoding { get; }
        public string ActionFullName { get; }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public readonly Func<object, object[], object> FastInvokeDelegate;
        
        /// <summary>
        /// Контроллер для активации через IoC.
        /// </summary>
        public Type ControllerType { get; }

        public ControllerActionMeta(string actionFullName, Type controllerType, MethodInfo methodInfo)
        {
            ActionFullName = actionFullName;
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
