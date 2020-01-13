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

        public T GetProxy<T>(ManagedConnection connection)
        {
            return GetProxy<T, ServerInterfaceProxy>((controllerName, p) => 
            {
                p.Initialize(controllerName, connection);
            });
        }

        public T GetProxy<T>(Func<ValueTask<ManagedConnection>> contextCallback)
        {
            return GetProxy<T, ClientInterfaceProxy>((controllerName, p) => 
            {
                p.Initialize(controllerName, contextCallback);
            });
        }

        private T GetProxy<T, TProxy>(Action<string, TProxy> initializeCopy) where TProxy : class, IInterfaceProxy
        {
            Type interfaceType = typeof(T);
            lock (_instanceDict)
            {
                if (_instanceDict.TryGetValue(interfaceType, out object proxy))
                {
                    return (T)proxy;
                }
                else
                {
                    var attrib = typeof(T).GetCustomAttribute<ControllerContractAttribute>(inherit: false);
                    if (attrib == null)
                        throw new ArgumentNullException($"Укажите имя контроллера или пометьте интерфейс {typeof(T).FullName} атрибутом \"{nameof(ControllerContractAttribute)}\".");

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
                            createdStatic = ProxyBuilder<TProxy>.CreateProxy<T>(instance: null);// TypeProxy.Create<T, TProxy>();
                            _staticDict.Add(interfaceType, createdStatic);
                        }
                    }

                    if (createdStatic == null)
                    {
                        var clone = p.Clone<TProxy>(); // Клонирование можно выполнять одновременно разными потоками.
                        initializeCopy(attrib.ControllerName, clone);
                        p = clone;
                    }
                    else
                    {
                        initializeCopy(attrib.ControllerName, createdStatic);
                        p = createdStatic;
                    }

                    _instanceDict.Add(interfaceType, p);
                    return (T)p;
                }
            }
        }
    }
}
