﻿using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Decorator;
using System.Diagnostics;
using System.Globalization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит прокси созданные из интерфейсов.
    /// </summary>
    internal sealed class ProxyCache
    {
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private static readonly Dictionary<Type, IInterfaceProxy> _staticDict = new();
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private readonly Dictionary<Type, IInterfaceProxy> _instanceDict = new();

        private static void InitializePropxy<T>(string controllerName, ServerInterfaceProxy<T> p, RpcManagedConnection connection) where T : class
        {
            p.InitializeClone(controllerName, connection);
        }

        private static void InitializeAsyncPropxy<T>(string controllerName, ClientInterfaceProxy<T> p, VRpcClient rpcClient) where T : class
        {
            p.InitializeClone(rpcClient, controllerName);
        }

        internal ServerInterfaceProxy<TIface> GetProxyDecorator<TIface>(RpcManagedConnection connection) where TIface : class
        {
            return GetOrCreateProxy<ServerInterfaceProxy<TIface>, TIface, RpcManagedConnection>(InitializePropxy, connection);
        }

        internal ClientInterfaceProxy<TIface> GetProxyDecorator<TIface>(VRpcClient rpcClient) where TIface : class
        {
            return GetOrCreateProxy<ClientInterfaceProxy<TIface>, TIface, VRpcClient>(InitializeAsyncPropxy, rpcClient);
        }

        private TClass GetOrCreateProxy<TClass, TIface, TArg1>(Action<string, TClass, TArg1> initializeClone, TArg1 arg1) 
            where TClass : class, IInterfaceDecorator<TIface>, IInterfaceProxy where TIface : class
        {
            Type interfaceType = typeof(TIface);
            Type classType = typeof(TClass);
            lock (_instanceDict)
            {
                if (_instanceDict.TryGetValue(classType, out IInterfaceProxy? proxy))
                {
                    var ret = proxy as TClass;
                    Debug.Assert(ret != null);
                    return ret;
                }
                else
                {
                    string controllerName = GetControllerNameFromInterface(interfaceType);
                    
                    IInterfaceProxy? p;
                    TClass? createdStatic;
                    lock (_staticDict) // Нужна блокировка на статичный словарь.
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

                    if (p != null)
                    {
                        // Клонирование можно выполнять одновременно разными потоками.
                        var clone = p.Clone<TClass>();
                        initializeClone(controllerName, clone, arg1);
                        p = clone;
                    }
                    else
                    {
                        Debug.Assert(createdStatic != null);
                        initializeClone(controllerName, createdStatic, arg1);
                        p = createdStatic;
                    }

                    _instanceDict.Add(classType, p);
                    var ret = p as TClass;
                    Debug.Assert(ret != null);
                    return ret;
                }
            }
        }

        private static string GetControllerNameFromInterface(Type interfaceType)
        {
            var attrib = interfaceType.GetCustomAttribute<ControllerContractAttribute>(inherit: false);
            if (attrib != null)
            {
                if (attrib.ControllerName.EndsWith("Controller", StringComparison.Ordinal))
                {
                    return attrib.ControllerName.TrimEnd("Controller", StringComparison.Ordinal);
                }
                else
                    return attrib.ControllerName;
            }
            else
            {
                return ControllerNameFromTypeName(interfaceType);
            }
        }

        private static string ControllerNameFromTypeName(Type interfaceType)
        {
#if NETSTANDARD2_0 || NET472
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
                ThrowHelper.ThrowVRpcException($"Пометьте интерфейс {interfaceType.FullName} атрибутом [ControllerContract] или задайте другое имя.");
                return default;
            }
        }
    }
}
