using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class LockedDictionary<TKey, TValue> : IConcurrentDictionary<TKey, TValue>
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> factory, TArg factoryArgument)
        {
            lock (_dict)
            {
                if (_dict.TryGetValue(key, out TValue value))
                {
                    return value;
                }
                else
                {
                    value = factory(key, factoryArgument);
                    _dict.Add(key, value);
                    return value;
                }
            }
        }
    }
}
