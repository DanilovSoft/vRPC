using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Count = {_list.Count}\}")]
    internal sealed class ClientConnections : ICollection<OldServerSideConnection>
    {
        /// <summary>
        /// Модификация коллекции <see cref="VRpcListener._connections"/> допускается с захватом этой блокировки.
        /// </summary>
        public readonly object SyncObj = new();
        private readonly List<OldServerSideConnection> _list;
        public int Count => _list.Count;
        public bool IsReadOnly => false;

        public ClientConnections()
        {
            _list = new List<OldServerSideConnection>();
        }

        public OldServerSideConnection this[int index]
        {
            get => _list[index];
            set => _list[index] = value;
        }

        internal void Remove(OldServerSideConnection context)
        {
            _list.Remove(context);
        }

        internal void Clear()
        {
            _list.Clear();
        }

        public void Add(OldServerSideConnection item)
        {
            _list.Add(item);
        }

        void ICollection<OldServerSideConnection>.Clear()
        {
            _list.Clear();
        }

        public bool Contains(OldServerSideConnection item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(OldServerSideConnection[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        bool ICollection<OldServerSideConnection>.Remove(OldServerSideConnection item)
        {
            return _list.Remove(item);
        }

        public IEnumerator<OldServerSideConnection> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int IndexOf(OldServerSideConnection con)
        {
            return _list.IndexOf(con);
        }
    }
}
