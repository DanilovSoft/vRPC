using DanilovSoft.vRPC.Controllers;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal sealed class InvokeActionsDictionary
    {
        /// <summary>
        /// Словарь используемый только для чтения.
        /// </summary>
        private readonly Dictionary<string, ControllerAction> _actionsDict;

        public InvokeActionsDictionary(Dictionary<string, Type> controllers)
        {
            // Методы типа "home/hello" без учета регистра.
            _actionsDict = new Dictionary<string, ControllerAction>(StringComparer.OrdinalIgnoreCase)
            {
                { "/SignIn", new ControllerAction(typeof(AccountController), AccountController.SignInMethod) },
                { "/SignOut", new ControllerAction(typeof(AccountController), AccountController.SignInMethod) }
            };

            foreach (KeyValuePair<string, Type> controller in controllers)
            {
                MethodInfo[] methods = controller.Value.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (MethodInfo method in methods)
                {
                    string actionFullName = $"{controller.Key}{GlobalVars.ControllerNameSplitter}{method.Name}";
                    _actionsDict.Add(actionFullName, new ControllerAction(controller.Value, method));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAction(string actionFullName, out ControllerAction value)
        {
            return _actionsDict.TryGetValue(actionFullName, out value);
        }
    }
}
