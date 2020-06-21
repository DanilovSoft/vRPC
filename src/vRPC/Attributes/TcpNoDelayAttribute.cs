using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Отключает алгоритм Нейгла при отправке запроса или ответа.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class TcpNoDelayAttribute : Attribute
    {
    }
}
