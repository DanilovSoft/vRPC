using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class ControllerActionsDictionary
    {
        /// <summary>
        /// Словарь используемый только для чтения.
        /// </summary>
        private readonly Dictionary<(Type, string), ControllerAction> _actionsDict;
        /// <summary>
        /// Словарь используемый только для чтения.
        /// Хранит все доступные контроллеры.
        /// </summary>
        public Dictionary<string, Type> Controllers { get; }

        public ControllerActionsDictionary(Dictionary<string, Type> controllers)
        {
            Controllers = controllers;

            _actionsDict = new Dictionary<(Type, string), ControllerAction>();

            foreach (KeyValuePair<string, Type> controller in controllers)
            {
                MethodInfo[] methods = controller.Value.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (MethodInfo method in methods)
                {
                    _actionsDict.Add((controller.Value, method.Name), new ControllerAction(method, $"{controller.Key}\\{method.Name}"));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(Type controllerType, string actionName, out ControllerAction value)
        {
            return _actionsDict.TryGetValue((controllerType, actionName), out value);
        }
    }
}
