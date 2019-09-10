using System;
using System.Collections.Generic;
using System.Text;

#if NET461 || NETSTANDARD2_0

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Существует только для Net Framework.
    /// </summary>
    internal static class CompatibilityExtensionMethods
    {
        /// <summary>
        /// Перегрузка для Net Framework.
        /// Attempts to add the specified key and value to the dictionary.
        /// </summary>
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if(!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Перегрузка для Net Framework.
        /// </summary>
        public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if(dictionary.TryGetValue(key, out value))
            {
                dictionary.Remove(key);
                return true;
            }
            return false;
        }
    }
}
#endif