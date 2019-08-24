using System.Reflection;

namespace DynamicMethodsLib
{
    public abstract class TypeProxy
    {
        public static TProxy Create<T, TProxy>()
        {
            return ProxyBuilder<TProxy>.CreateProxy<T>(instance: null);
        }

        public static TProxy Create<T, TProxy>(object instance)
        {
            return ProxyBuilder<TProxy>.CreateProxy<T>(instance: instance);
        }

        public abstract object Invoke(MethodInfo targetMethod, object[] args);
    }
}
