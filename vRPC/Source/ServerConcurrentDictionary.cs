using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> factory, TArg factoryArgument)
        {
#if NETSTANDARD2_0
            return _dict.GetOrAdd(key, key => factory(key, factoryArgument));
#else
            return _dict.GetOrAdd(key, factory, factoryArgument);
#endif
        }
    }
}
