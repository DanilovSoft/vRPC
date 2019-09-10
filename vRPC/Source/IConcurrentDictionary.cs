using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal interface IConcurrentDictionary<TKey, TValue>
    {
        TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory);
    }
}
