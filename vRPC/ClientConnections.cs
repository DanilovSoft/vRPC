using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    [DebuggerDisplay(@"\{Count = {_list.Count}\}")]
    internal sealed class ClientConnections : ICollection<ServerSideConnection>
    {
        /// <summary>
        /// Модификация коллекции допускается с захватом этой блокировки.
        /// </summary>
        public readonly object SyncObj = new object();
        private readonly List<ServerSideConnection> _list = new List<ServerSideConnection>();
        public int Count => _list.Count;
        public bool IsReadOnly => false;

        public ClientConnections()
        {

        }

        internal void Remove(ServerSideConnection context)
        {
            _list.Remove(context);
        }

        internal void Clear()
        {
            _list.Clear();
        }

        public void Add(ServerSideConnection item)
        {
            _list.Add(item);
        }

        void ICollection<ServerSideConnection>.Clear()
        {
            _list.Clear();
        }

        public bool Contains(ServerSideConnection item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(ServerSideConnection[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        bool ICollection<ServerSideConnection>.Remove(ServerSideConnection item)
        {
            return _list.Remove(item);
        }

        public IEnumerator<ServerSideConnection> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
