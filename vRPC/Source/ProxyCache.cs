using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Decorator;
using System.Diagnostics;

namespace DanilovSoft.vRPC
{
    internal sealed class ProxyCache
    {
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private static readonly Dictionary<Type, IInterfaceProxy> _staticDict = new Dictionary<Type, IInterfaceProxy>();
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private readonly Dictionary<Type, IInterfaceProxy> _instanceDict = new Dictionary<Type, IInterfaceProxy>();

        public ProxyCache()
        {

        }

        [DebuggerStepThrough]
        private static void InitializePropxy<T>(string controllerName, ServerInterfaceProxy<T> p, ManagedConnection connection) where T : class
        {
            p.InitializeClone(controllerName, connection);
        }

        [DebuggerStepThrough]
        private static void InitializeAsyncPropxy<T>(string controllerName, ClientInterfaceProxy<T> p, RpcClient rpcClient) where T : class
        {
            p.InitializeClone(rpcClient, controllerName);
        }

        internal ServerInterfaceProxy<TIface> GetProxyDecorator<TIface>(ManagedConnection connection) where TIface : class
        {
            return GetProxy<TIface, ServerInterfaceProxy<TIface>, ManagedConnection>(InitializePropxy, connection);
        }

        internal ClientInterfaceProxy<TIface> GetProxyDecorator<TIface>(RpcClient rpcClient) where TIface : class
        {
            return GetProxy<TIface, ClientInterfaceProxy<TIface>, RpcClient>(InitializeAsyncPropxy, rpcClient);
        }

        private TClass GetProxy<TIface, TClass, TArg1>(Action<string, TClass, TArg1> initializeClone, TArg1 arg1) 
            where TClass : class, IInterfaceDecorator<TIface>, IInterfaceProxy where TIface : class
        {
            Type interfaceType = typeof(TIface);
            Type classType = typeof(TClass);
            lock (_instanceDict)
            {
                if (_instanceDict.TryGetValue(classType, out IInterfaceProxy proxy))
                {
                    return proxy as TClass;
                }
                else
                {
                    string controllerName = GetControllerNameFromInterface(interfaceType);
                    
                    IInterfaceProxy p;
                    TClass createdStatic;
                    lock (_staticDict) // Нужна блокировка на статический словарь.
                    {
                        if (_staticDict.TryGetValue(interfaceType, out p))
                        {
                            createdStatic = null;
                        }
                        else
                        {
                            createdStatic = ProxyBuilder<TClass>.CreateProxy<TIface>(instance: null);
                            _staticDict.Add(interfaceType, createdStatic);
                        }
                    }

                    if (createdStatic == null)
                    {
                        // Клонирование можно выполнять одновременно разными потоками.
                        var clone = p.Clone<TClass>();
                        initializeClone(controllerName, clone, arg1);
                        p = clone;
                    }
                    else
                    {
                        initializeClone(controllerName, createdStatic, arg1);
                        p = createdStatic;
                    }

                    _instanceDict.Add(classType, p);
                    return p as TClass;
                }
            }
        }

        private static string GetControllerNameFromInterface(Type interfaceType)
        {
            string controllerName;
            var attrib = interfaceType.GetCustomAttribute<ControllerContractAttribute>(inherit: false);
            if (attrib != null)
            {
                controllerName = attrib.ControllerName;
            }
            else
            {
                controllerName = ControllerNameFromTypeName(interfaceType);
            }
            return controllerName;
        }

        private static string ControllerNameFromTypeName(Type interfaceType)
        {
#if NETSTANDARD2_0
            bool startsWithI = interfaceType.Name.StartsWith("I", StringComparison.Ordinal);
#else
            bool startsWithI = interfaceType.Name.StartsWith('I');
#endif

            // Нужно игнорировать окончание 'Controller'
            int controllerWordIndex = interfaceType.Name.LastIndexOf("Controller", StringComparison.OrdinalIgnoreCase);

            string controllerName;
            if (controllerWordIndex != -1)
            // Оканчивается на 'Controller'.
            {
                if (startsWithI)
                {
                    controllerName = interfaceType.Name.Substring(1, controllerWordIndex - 1);
                }
                else
                {
                    controllerName = interfaceType.Name.Substring(0, controllerWordIndex);
                }
            }
            else
            // Не оканчивается на 'Controller'.
            {
                if (startsWithI)
                {
                    controllerName = interfaceType.Name.Substring(1);
                }
                else
                {
                    controllerName = interfaceType.Name;
                }
            }

            if (controllerName.Length > 0)
            {
                return controllerName;
            }
            else
            {
                throw new VRpcException($"Пометьте интерфейс {interfaceType.FullName} атрибутом [ControllerContract] или задайте другое имя.", VRpcErrorCode.InvalidInterfaceName);
            }
        }
    }
}
