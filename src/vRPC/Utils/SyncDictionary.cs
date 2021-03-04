using System;
using System.Collections.Generic;

namespace DanilovSoft.vRPC
{
    internal sealed class SyncDictionary<TKey, TValue> where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _dict = new();

        public TValue GetOrAdd(TKey key, Func<TKey, Type, TValue> factory, Type returnType)
        {
            lock(_dict)
            {
                if(_dict.TryGetValue(key, out TValue value))
                {
                    return value;
                }
                else
                {
                    value = factory(key, returnType);
                    _dict.Add(key, value);
                    return value;
                }
            }
        }
    }
}
