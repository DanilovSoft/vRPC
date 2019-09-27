using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Служит для инъекции зависимостей. Фактически выполняет GetProxy&lt;<typeparamref name="T"/>&gt;() для текущего подключения.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IProxy<T>
    {
        /// <summary>
        /// Создаёт прокси из интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// </summary>
        T Interface { get; }
    }

    internal class ProxyFactory<T> : IProxy<T>
    {
        /// <summary>
        /// Прозрачный прокси к удалённой стороне.
        /// </summary>
        public T Interface { get; }

        public ProxyFactory(GetProxyScope getProxyScope)
        {
            Interface = getProxyScope.GetProxy.GetProxy<T>();
        }
    }
}
