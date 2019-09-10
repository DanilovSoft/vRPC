using System.Diagnostics;

namespace DanilovSoft.vRPC
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
