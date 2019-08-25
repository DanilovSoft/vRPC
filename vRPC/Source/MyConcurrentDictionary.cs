using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace vRPC
{
    internal sealed class MyConcurrentDictionary<TKey, TValue> : IConcurrentDictionary<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, TValue> _dict = new ConcurrentDictionary<TKey, TValue>();

        public MyConcurrentDictionary()
        {

        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            return _dict.GetOrAdd(key, valueFactory);
        }
    }
}
