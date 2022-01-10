using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{Count = {_dict.Count}\}")]
    internal sealed class ServerConcurrentDictionary<TKey, TValue> : IConcurrentDictionary<TKey, TValue> where TKey : notnull
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly ConcurrentDictionary<TKey, TValue> _dict = new();

        public ServerConcurrentDictionary()
        {

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
