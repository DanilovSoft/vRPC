using System;
using System.Collections.Generic;
using System.Reflection;

namespace DanilovSoft.vRPC
{
    internal static class GlobalVars
    {
        public static readonly Action DummyAction = delegate { throw new Exception(nameof(RequestAwaiter)); };

        public static Dictionary<string, Type> FindAllControllers(Assembly assembly)
        {
            var controllers = new Dictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            Type[] types = assembly.GetExportedTypes();

            foreach (Type controllerType in types)
            {
                if (controllerType.IsSubclassOf(typeof(Controller)))
                {
                    int ind = controllerType.Name.IndexOf("Controller", StringComparison.Ordinal);
                    if (ind != -1)
                    {
                        // Имя без учета окончания 'Controller'.
                        controllers.Add(controllerType.Name.Substring(0, ind), controllerType);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Контроллер типа {controllerType.FullName} должен заканчиваться словом 'Controller'.");
                    }
                }
            }
            return controllers;
        }
    }
}
