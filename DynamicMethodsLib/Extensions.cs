using DynamicMethodsLib;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace System.Reflection
{
    public static class MethodInfoExtensions
    {
        private static readonly ConcurrentDictionary<MethodInfo, Func<object, object[], object>> _methodsDict = 
            new ConcurrentDictionary<MethodInfo, Func<object, object[], object>>();

        /// <summary>
        /// Dynamic Method.
        /// </summary>
        //[DebuggerStepThrough]
        public static object InvokeFast(this MethodInfo methodInfo, object instance, object[] args, bool skipConvertion = true)
        {
            Func<object, object[], object> func = _methodsDict.GetOrAdd(methodInfo, m => DynamicMethodFactory.CreateMethodCall(m, skipConvertion));
            object result = func.Invoke(instance, args);
            return result;
        }
    }
}
