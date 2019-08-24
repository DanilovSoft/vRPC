using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    internal sealed class ProxyCache
    {
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private static readonly Dictionary<Type, InterfaceProxy> _staticProxies = new Dictionary<Type, InterfaceProxy>();
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private readonly Dictionary<Type, object> _proxies = new Dictionary<Type, object>();

        public T GetProxy<T>(Func<ValueTask<Context>> contextCallback)
        {
            Type interfaceType = typeof(T);
            lock (_proxies)
            {
                if (_proxies.TryGetValue(interfaceType, out object proxy))
                {
                    return (T)proxy;
                }
                else
                {
                    var attrib = typeof(T).GetCustomAttribute<ControllerContractAttribute>(inherit: false);
                    if (attrib == null)
                        throw new ArgumentNullException("controllerName", $"Укажите имя контроллера или пометьте интерфейс атрибутом \"{nameof(ControllerContractAttribute)}\"");

                    bool existStatic;
                    InterfaceProxy p;
                    lock (_staticProxies) // Нужна блокировка на статический словарь.
                    {
                        if (_staticProxies.TryGetValue(interfaceType, out p))
                        {
                            existStatic = true;
                        }
                        else
                        {
                            existStatic = false;
                            p = TypeProxy.Create<T, InterfaceProxy>();
                            _staticProxies.Add(interfaceType, p);
                        }
                    }

                    if(existStatic)
                        p = p.Clone(); // Клонирование можно выполнять одновременно разными потоками.

                    p.SetCallback(attrib.ControllerName, contextCallback);

                    _proxies.Add(interfaceType, p);
                    return (T)(object)p;
                }
            }
        }
    }
}
