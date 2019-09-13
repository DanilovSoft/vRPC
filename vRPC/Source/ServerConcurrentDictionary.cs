using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class ServerConcurrentDictionary<TKey, TValue> : IConcurrentDictionary<TKey, TValue>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly ConcurrentDictionary<TKey, TValue> _dict = new ConcurrentDictionary<TKey, TValue>();

        public ServerConcurrentDictionary()
        {

        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            return _dict.GetOrAdd(key, valueFactory);
        }
    }
}
