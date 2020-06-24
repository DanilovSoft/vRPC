using DanilovSoft.vRPC.Source;
using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит исчерпывающую информацию о методе контроллера.
    /// Этот клас переиспользуется.
    /// </summary>
    [DebuggerDisplay(@"\{{TargetMethod.GetControllerActionName()}\}")]
    internal sealed class ControllerActionMeta
    {
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
        public int[] DisposableArgsIndex { get; }

        public ControllerActionMeta(string actionFullName, Type controllerType, MethodInfo methodInfo)
        {
            ActionFullName = actionFullName;
            ControllerType = controllerType;
            TargetMethod = methodInfo;
            Parametergs = methodInfo.GetParameters();

            DisposableArgsIndex = GetDisposableArgsIndex(Parametergs);

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
                ProducesEncoding = KnownEncoding.JsonEncoding;
            }

            FastInvokeDelegate = DynamicMethodFactory.CreateMethodCall(methodInfo, skipConvertion: true);
        }

        private static int[] GetDisposableArgsIndex(ParameterInfo[] parametergs)
        {
            return Indexes(parametergs).ToArray();

            static IEnumerable<int> Indexes(ParameterInfo[] parametergs)
            {
                for (int i = 0; i < parametergs.Length; i++)
                {
                    if (typeof(IDisposable).IsAssignableFrom(parametergs[i].ParameterType))
                    {
                        yield return i;
                    }
                }
            }
        }

        public void DisposeArgs(object?[] args)
        {
            for (int i = 0; i < DisposableArgsIndex.Length; i++)
            {
                int argIndex = DisposableArgsIndex[i];

                object? arg = Interlocked.Exchange(ref args[argIndex], null);

                // Массив может быть не полностью инициирован.
                if (arg is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
