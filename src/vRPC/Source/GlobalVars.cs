using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace DanilovSoft.vRPC
{
    internal static class GlobalVars
    {
        internal const char ControllerNameSplitter = '/';
        public static readonly Action SentinelAction = delegate { Debug.Assert(false); throw new Exception(nameof(ResponseAwaiter)); };
        private static RecyclableMemoryStreamManager? _memoryManager;
        public static RecyclableMemoryStreamManager RecyclableMemory => LazyInitializer.EnsureInitialized(ref _memoryManager, () => new RecyclableMemoryStreamManager());

        public static void Initialize(RecyclableMemoryStreamManager memoryManager)
        {
            if (Interlocked.CompareExchange(ref _memoryManager, memoryManager, null) != null)
                throw new InvalidOperationException("MemoryManager уже инициализирован");
        }

        public static Dictionary<string, Type> FindAllControllers(Assembly assembly)
        {
            var controllers = new Dictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            Type[] types = assembly.GetTypes();//.GetExportedTypes();

            foreach (Type controllerType in types)
            {
                if (controllerType.IsSubclassOf(typeof(Controller)))
                {
                    if (controllerType.IsPublic || IsInternal(controllerType))
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
            }
            return controllers;
        }

        private static bool IsInternal(Type t)
        {
            return
                !t.IsVisible
                && !t.IsPublic
                && t.IsNotPublic
                && !t.IsNested
                && !t.IsNestedPublic
                && !t.IsNestedFamily
                && !t.IsNestedPrivate
                && !t.IsNestedAssembly
                && !t.IsNestedFamORAssem
                && !t.IsNestedFamANDAssem;
        }
    }
}
