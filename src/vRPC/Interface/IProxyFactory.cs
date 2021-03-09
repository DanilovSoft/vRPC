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
        string? RemoteControllerName { get; }
    }

    // Это единственная имплементация для IProxy<T>.
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TIface">Интерфейс пользователя для которого нужно создать прокси.</typeparam>
    [DebuggerDisplay("{Proxy}")]
    internal sealed class ProxyFactory<TIface> : IProxy<TIface> where TIface : class
    {
        /// <summary>
        /// Прозрачный прокси к удалённой стороне.
        /// </summary>
        public TIface Proxy { get; }
        public string? RemoteControllerName => ((IInterfaceDecorator<TIface>)Proxy).ControllerName;

        // Вызывается через рефлексию.
        public ProxyFactory(RequestContextScope getProxyScope)
        {
            Debug.Assert(getProxyScope.Connection != null);

            Proxy = getProxyScope.Connection.GetProxy<TIface>();
        }
    }
}
