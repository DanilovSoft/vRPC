using System;

namespace vRPC
{
    /// <summary>
    /// Указывает имя контроллера к которому будут осуществляться вызовы через помеченный интерфейс.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    public class ControllerContractAttribute : Attribute
    {
        public string ControllerName { get; }

        public ControllerContractAttribute(string controllerName)
        {
            ControllerName = controllerName;
        }
    }
}
