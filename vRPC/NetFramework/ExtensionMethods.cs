using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

#if NET461 || NETSTANDARD2_0 || NET472

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

        //public static bool StartsWith(this string str, char value)
        //{

        //}
    }
}

namespace System
{
    internal static class CompatibilityExtensionMethods
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf(this string str, char value, StringComparison _)
        {
            int index = str.IndexOf(value);
            return index;
        }
    }
}
#endif