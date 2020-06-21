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
        public delegate void TestMethodDelegate<T>();

        public Action<Stream, object> SerializerDelegate { get; }
        public MethodInfo TargetMethod { get; }
        public ParameterInfo[] Parametergs { get; }
        /// <summary>
        /// Формат возвращаемых данных.
        /// </summary>
        public string ProducesEncoding { get; }
        public string ActionFullName { get; }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)] // Отладчик путает педали.
        public Func<object, object[], object?> FastInvokeDelegate { get; }
        
        /// <summary>
        /// Контроллер для активации через IoC.
        /// </summary>
        public Type ControllerType { get; }
        public bool TcpNoDelay { get; }

        public ControllerActionMeta(string actionFullName, Type controllerType, MethodInfo methodInfo)
        {
            ActionFullName = actionFullName;
            ControllerType = controllerType;
            TargetMethod = methodInfo;
            Parametergs = methodInfo.GetParameters();

            TcpNoDelay = Attribute.IsDefined(methodInfo, typeof(TcpNoDelayAttribute));

            var protobufAttrib = methodInfo.GetCustomAttribute<ProducesProtoBufAttribute>();
            if (protobufAttrib != null)
            {
                SerializerDelegate = ExtensionMethods.SerializeObjectProtobuf;
                ProducesEncoding = ProducesProtoBufAttribute.Encoding;
            }
            else
            {
                // Сериализатор по умолчанию — Json.
                SerializerDelegate = ExtensionMethods.SerializeObjectJson;
                ProducesEncoding = "json";
            }

            FastInvokeDelegate = DynamicMethodFactory.CreateMethodCall(methodInfo, skipConvertion: true);

            var method = GetType().GetMethod("TestMe");

            var genericMethod = method.MakeGenericMethod(typeof(int));

            Delegate.CreateDelegate(typeof(Action), genericMethod);

            //var deleg = genericMethod.CreateDelegate
        }

        private void DynamicMethod()
        {
            TestMe<int>();
        }

        public void TestMe<T>()
        {

        }
    }
}
