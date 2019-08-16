using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    internal sealed class ControllerActionsDictionary
    {
        private readonly Dictionary<(Type, string), ControllerAction> _actions;

        public ControllerActionsDictionary(Dictionary<string, Type> controllers)
        {
            _actions = new Dictionary<(Type, string), ControllerAction>();

            foreach (KeyValuePair<string, Type> controller in controllers)
            {
                MethodInfo[] methods = controller.Value.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (MethodInfo method in methods)
                {
                    _actions.Add((controller.Value, method.Name), new ControllerAction(method, $"{controller.Key}\\{method.Name}"));
                }
            }
        }

        public bool TryGetValue(Type controllerType, string actionName, out ControllerAction value)
        {
            return _actions.TryGetValue((controllerType, actionName), out value);
        }
    }
}
