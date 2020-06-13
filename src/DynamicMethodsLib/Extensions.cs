﻿using DynamicMethodsLib;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Reflection
{
    public static class MethodInfoExtensions
    {
        private static readonly ConcurrentDictionary<MethodInfo, Func<object, object[], object>> _methodsDict = 
            new ConcurrentDictionary<MethodInfo, Func<object, object[], object>>();

        /// <summary>
        /// Dynamic Method.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object InvokeFast(this MethodInfo methodInfo, object instance, object[] args)
        {
            Func<object, object[], object> func = _methodsDict.GetOrAdd(methodInfo, m => DynamicMethodFactory.CreateMethodCall(m, skipConvertion: true));
            object result = func.Invoke(instance, args);
            return result;
        }
    }
}