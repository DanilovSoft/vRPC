using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal static class Warmup
    {
        /// <summary>
        /// Выполняет AOT(Ahead-Of-Time) оптимизацию.
        /// </summary>
        private static void DoWarmup()
        {
            RuntimeHelpers.RunClassConstructor(typeof(RpcManagedConnection).TypeHandle);

#pragma warning disable CA2012 // Используйте ValueTasks правильно
            DynamicAwaiter.ConvertToTask(new ValueTask());
#pragma warning restore CA2012 // Используйте ValueTasks правильно
        }

#if !NET472 && !NETSTANDARD2_0 && !NETCOREAPP3_1
        [ModuleInitializer]
#endif
        internal static void InitializeModule()
        {
            DoWarmup();
        }
    }
}
