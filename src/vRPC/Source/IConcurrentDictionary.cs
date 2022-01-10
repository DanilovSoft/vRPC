using System;

namespace DanilovSoft.vRPC
{
    internal interface IConcurrentDictionary<TKey, TValue> where TKey : notnull
    {
        TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> factory, TArg factoryArgument);
    }
}
