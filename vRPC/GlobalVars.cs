using System;
using System.Collections.Generic;
using System.Reflection;

namespace vRPC
{
    internal static class GlobalVars
    {
        public static readonly Action DummyAction = delegate { };

        public static Dictionary<string, Type> FindAllControllers(Assembly assembly)
        {
            var controllers = new Dictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            Type[] types = assembly.GetExportedTypes();

            foreach (Type controllerType in types)
            {
                if (controllerType.IsSubclassOf(typeof(Controller)))
                {
                    // Имя без учета окончания 'Controller'.
                    controllers.Add(controllerType.Name.Substring(0, controllerType.Name.IndexOf("Controller")), controllerType);
                }
            }
            return controllers;
        }
    }
}
