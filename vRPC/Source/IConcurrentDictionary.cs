using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal interface IConcurrentDictionary<TKey, TValue>
    {
        TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
        TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> factory, TArg factoryArgument);
    }
}
