using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Использует блокировку для GetOrAdd.
    /// </summary>
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class LockedDictionary<TKey, TValue> : IConcurrentDictionary<TKey, TValue> where TKey : notnull
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<TKey, TValue> _dict = new();

        [DebuggerStepThrough]
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
