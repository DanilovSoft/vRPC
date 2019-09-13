using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Метод помеченный атрибутом Notification не ожидает результата.
    /// Возвращаемый тип метода должен быть void или Task или ValueTask.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class NotificationAttribute : Attribute
    {
    }
}
