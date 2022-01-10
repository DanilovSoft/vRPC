namespace DanilovSoft.vRPC.Decorator
{
    internal static class DebugOnly
    {
        //[Conditional("DEBUG")]
        //public static void ValidateIsInstanceOfType(object? returnValue, Type returnType)
        //{
        //    if (returnValue != null)
        //    {
        //        Debug.Assert(returnType.IsInstanceOfType(returnValue), "Результат запроса не соответствует возвращаемому типу интерфейса");
        //    }
        //    else
        //    {
        //        if (returnType != typeof(void))
        //        {
        //            Debug.Assert(!returnType.IsValueType, "Результат запроса не соответствует возвращаемому типу интерфейса");
        //        }
        //    }
        //}
    }
}
