using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC.Decorator
{
    internal static class DebugOnly
    {
        [Conditional("DEBUG")]
        public static void ValidateReturnType(Type returnType, object returnValue)
        {
            if (returnValue != null)
            {
                Debug.Assert(returnType.IsInstanceOfType(returnValue), "Результат запроса не соответствует возвращаемому типу интерфейса");
            }
            else
            {
                if (returnType != typeof(void))
                {
                    Debug.Assert(!returnType.IsValueType, "Результат запроса не соответствует возвращаемому типу интерфейса");
                }
            }
        }
    }
}
