using DynamicMethodsLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    internal static class ProxyCache
    {
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private static readonly Dictionary<Type, object> _proxies = new Dictionary<Type, object>();

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// </summary>
        public static T GetProxy<T>(Func<ValueTask<Context>> contextCallback)
        {
            var attrib = typeof(T).GetCustomAttribute<ControllerContractAttribute>(inherit: false);
            if (attrib == null)
                throw new ArgumentNullException("controllerName", $"Укажите имя контроллера или пометьте интерфейс атрибутом \"{nameof(ControllerContractAttribute)}\"");

            return GetProxy<T>(attrib.ControllerName, contextCallback);
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// </summary>
        /// <param name="controllerName">Имя контроллера на удалённой стороне к которому применяется текущий интерфейс <see cref="{T}"/>.</param>
        public static T GetProxy<T>(string controllerName, Func<ValueTask<Context>> contextCallback)
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
                    T proxyT = TypeProxy.Create<T, InterfaceProxy>((contextCallback, controllerName));
                    _proxies.Add(interfaceType, proxyT);
                    return proxyT;
                }
            }
        }
    }
}
