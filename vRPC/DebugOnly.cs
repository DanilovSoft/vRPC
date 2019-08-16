using System.Diagnostics;

namespace vRPC
{
    internal static class DebugOnly
    {
        [DebuggerNonUserCode]
        [DebuggerStepThrough]
        [Conditional("DEBUG")]
        public static void Break()
        {
            if (Debugger.IsAttached)
                Debugger.Break();
        }
    }
}
