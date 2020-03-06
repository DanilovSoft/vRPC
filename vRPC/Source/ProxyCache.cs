using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Decorator;

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
        private readonly Dictionary<Type, object> _instanceDict = new Dictionary<Type, object>();

        public ProxyCache()
        {

        }

        private static void InitializePropxy(string controllerName, ServerInterfaceProxy p, ManagedConnection connection)
        {
            p.InitializeClone(controllerName, connection);
        }

        private static void InitializeAsyncPropxy(string controllerName, ClientInterfaceProxy p, RpcClient rpcClient)
        {
            p.InitializeClone(rpcClient, controllerName);
        }

        internal T GetProxy<T>(ManagedConnection connection) where T : class
        {
            return GetProxy<T, ServerInterfaceProxy, ManagedConnection>(InitializePropxy, connection);
        }

        internal T GetProxy<T>(RpcClient rpcClient) where T : class
        {
            return GetProxy<T, ClientInterfaceProxy, RpcClient>(InitializeAsyncPropxy, rpcClient);
        }

        private T GetProxy<T, TProxy, TCon>(Action<string, TProxy, TCon> initializeCopy, TCon con) where TProxy : class, IInterfaceProxy where T : class
        {
            Type interfaceType = typeof(T);
            lock (_instanceDict)
            {
                if (_instanceDict.TryGetValue(interfaceType, out object proxy))
                {
                    return proxy as T;
                }
                else
                {
                    string controllerName = GetControllerNameFromInterface(interfaceType);
                    
                    IInterfaceProxy p;
                    TProxy createdStatic;
                    lock (_staticDict) // Нужна блокировка на статический словарь.
                    {
                        if (_staticDict.TryGetValue(interfaceType, out p))
                        {
                            createdStatic = null;
                        }
                        else
                        {
                            createdStatic = ProxyBuilder<TProxy>.CreateProxy<T>(instance: null);
                            _staticDict.Add(interfaceType, createdStatic);
                        }
                    }

                    if (createdStatic == null)
                    {
                        var clone = p.Clone<TProxy>(); // Клонирование можно выполнять одновременно разными потоками.
                        initializeCopy(controllerName, clone, con);
                        p = clone;
                    }
                    else
                    {
                        initializeCopy(controllerName, createdStatic, con);
                        p = createdStatic;
                    }

                    _instanceDict.Add(interfaceType, p);
                    return p as T;
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
                throw new VRpcException($"Пометьте интерфейс {interfaceType.FullName} атрибутом [ControllerContract] или задайте другое имя.");
            }
        }
    }
}
