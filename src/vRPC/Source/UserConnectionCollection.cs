using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Потокобезопасный список авторизованных соединений пользователя.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    [DebuggerTypeProxy(typeof(TypeProxy))]
    public class UserConnectionCollection : IList<ServerSideConnection>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"Count = {_list.Count}" + "}";
        internal readonly object SyncRoot = new object();
        /// <summary>
        /// Доступ осуществляется только через блокировку <see cref="SyncRoot"/>.
        /// </summary>
        private readonly List<ServerSideConnection> _list = new List<ServerSideConnection>();
        /// <summary>
        /// Доступ осуществляется только через блокировку <see cref="SyncRoot"/>.
        /// Если коллекция уже была удалена из словаря подключений, то значение будет <see langword="true"/> 
        /// и испольльзовать этот экземпляр больше нельзя.
        /// </summary>
        public bool IsDestroyed { get; set; }

        public ServerSideConnection this[int index]
        {
            get
            {
                lock(SyncRoot)
                {
                    return _list[index];
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    _list[index] = value;
                }
            }
        }

        public int Count
        {
            get
            {
                lock(SyncRoot)
                {
                    return _list.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public void Add(ServerSideConnection context)
        {
            lock(SyncRoot)
            {
                _list.Add(context);
            }
        }

        public void Clear()
        {
            lock(SyncRoot)
            {
                _list.Clear();
            }
        }

        public bool Contains(ServerSideConnection context)
        {
            lock(SyncRoot)
            {
                return _list.Contains(context);
            }
        }

        public void CopyTo(ServerSideConnection[] array, int arrayIndex)
        {
            lock(SyncRoot)
            {
                _list.CopyTo(array, arrayIndex);
            }
        }

        public int IndexOf(ServerSideConnection context)
        {
            lock(SyncRoot)
            {
                return _list.IndexOf(context);
            }
        }

        public void Insert(int index, ServerSideConnection context)
        {
            lock(SyncRoot)
            {
                _list.Insert(index, context);
            }
        }

        public bool Remove(ServerSideConnection context)
        {
            lock(SyncRoot)
            {
                return _list.Remove(context);
            }
        }

        public void RemoveAt(int index)
        {
            lock (SyncRoot)
            {
                _list.RemoveAt(index);
            }
        }

        /// <summary>
        /// Возвращает копию своей коллекции.
        /// </summary>
        public IEnumerator<ServerSideConnection> GetEnumerator()
        {
            lock(SyncRoot)
            {
                return _list.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #region Debug
#pragma warning disable CA1812
        [DebuggerNonUserCode]
#pragma warning disable CA1812
        private class TypeProxy
        {
            private readonly UserConnectionCollection _self;
            public TypeProxy(UserConnectionCollection self)
            {
                _self = self;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public ServerSideConnection[] Items => _self._list.ToArray();
        }
#pragma warning restore CA1812
        #endregion
    }
}
