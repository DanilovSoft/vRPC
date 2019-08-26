namespace vRPC
{
    internal static class Warmup
    {
        /// <summary>
        /// Выполняет AOT(Ahead-Of-Time) оптимизацию.
        /// </summary>
        public static void DoWarmup()
        {
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ManagedConnection).TypeHandle);
        }
    }
}
