using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace vRPC
{
    [DebuggerDisplay(@"\{Count = {_list.Count}\}")]
    internal sealed class ClientConnections : ICollection<ClientContext>
    {
        /// <summary>
        /// Модификация коллекции допускается с захватом этой блокировки.
        /// </summary>
        public readonly object SyncObj = new object();
        private readonly List<ClientContext> _list = new List<ClientContext>();
        public int Count => _list.Count;
        public bool IsReadOnly => false;

        public ClientConnections()
        {

        }

        internal void Remove(ClientContext context)
        {
            _list.Remove(context);
        }

        internal void Clear()
        {
            _list.Clear();
        }

        public void Add(ClientContext item)
        {
            _list.Add(item);
        }

        void ICollection<ClientContext>.Clear()
        {
            _list.Clear();
        }

        public bool Contains(ClientContext item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(ClientContext[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        bool ICollection<ClientContext>.Remove(ClientContext item)
        {
            return _list.Remove(item);
        }

        public IEnumerator<ClientContext> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
