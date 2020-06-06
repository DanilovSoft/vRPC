using DanilovSoft.vRPC.Decorator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        /// Прокси из интерфейса.
        /// </summary>
        T Proxy { get; }
        /// <summary>
        /// Имя удалённого контроллера с которым связан прокси.
        /// </summary>
        string RemoteControllerName { get; }
    }

    // Это единственная имплементация для IProxy<T>.
    [DebuggerDisplay("{Proxy}")]
    internal sealed class ProxyFactory<T> : IProxy<T> where T : class
    {
        /// <summary>
        /// Прозрачный прокси к удалённой стороне.
        /// </summary>
        public T Proxy { get; }
        public string RemoteControllerName => ((IInterfaceDecorator<T>)Proxy).ControllerName;

        // Вызывается через рефлексию.
        public ProxyFactory(GetProxyScope getProxyScope)
        {
            Proxy = getProxyScope.GetProxy.GetProxy<T>();
        }
    }
}
