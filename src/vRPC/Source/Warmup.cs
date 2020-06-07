using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal static class Warmup
    {
        /// <summary>
        /// Выполняет AOT(Ahead-Of-Time) оптимизацию.
        /// </summary>
        public static void DoWarmup()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ManagedConnection).TypeHandle);

#pragma warning disable CA2012 // Используйте ValueTasks правильно
            DynamicAwaiter.ConvertToTask(new ValueTask());
#pragma warning restore CA2012 // Используйте ValueTasks правильно
        }
    }
}
