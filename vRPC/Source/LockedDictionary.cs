using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class LockedDictionary<TKey, TValue> : IConcurrentDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, TValue> _dict = new Dictionary<TKey, TValue>();

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
        {
            lock (_dict)
            {
                if (_dict.TryGetValue(key, out TValue value))
                {
                    return value;
                }
                else
                {
                    value = factory(key);
                    _dict.Add(key, value);
                    return value;
                }
            }
        }
    }
}
