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
        private readonly Dictionary<Type, object> _proxies = new Dictionary<Type, object>();

        private static class StaticProxyCache
        {
            /// <summary>
            /// Содержит прокси созданные из интерфейсов.
            /// </summary>
            private static readonly Dictionary<Type, InterfaceProxy> _staticProxies = new Dictionary<Type, InterfaceProxy>();

            /// <summary>
            /// Создает прокси к методам удалённой стороны на основе интерфейса.
            /// </summary>
            public static InterfaceProxy GetProxy<T>(out bool createdNew)
            {
                var attrib = typeof(T).GetCustomAttribute<ControllerContractAttribute>(inherit: false);
                if (attrib == null)
                    throw new ArgumentNullException("controllerName", $"Укажите имя контроллера или пометьте интерфейс атрибутом \"{nameof(ControllerContractAttribute)}\"");

                return GetProxy<T>(attrib.ControllerName, out createdNew);
            }

            /// <summary>
            /// Создает прокси к методам удалённой стороны на основе интерфейса.
            /// </summary>
            /// <param name="controllerName">Имя контроллера на удалённой стороне к которому применяется текущий интерфейс <see cref="{T}"/>.</param>
            private static InterfaceProxy GetProxy<T>(string controllerName, out bool createdNew)
            {
                Type interfaceType = typeof(T);
                lock (_staticProxies)
                {
                    if (_staticProxies.TryGetValue(interfaceType, out InterfaceProxy proxy))
                    {
                        createdNew = false;
                        return proxy;
                    }
                    else
                    {
                        var proxyT = (InterfaceProxy)(object)TypeProxy.Create<T, InterfaceProxy>(controllerName);
                        _staticProxies.Add(interfaceType, proxyT);
                        createdNew = true;
                        return proxyT;
                    }
                }
            }
        }

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
                    InterfaceProxy proxyT = StaticProxyCache.GetProxy<T>(out bool createdNew);
                    if (createdNew)
                    {
                        proxyT.SetCallback(contextCallback);
                        _proxies.Add(interfaceType, proxyT);
                        return (T)(object)proxyT;
                    }
                    else
                    {
                        var clone = proxyT.Clone(contextCallback);
                        _proxies.Add(interfaceType, clone);
                        return (T)(object)clone;
                    }
                }
            }
        }
    }
}
