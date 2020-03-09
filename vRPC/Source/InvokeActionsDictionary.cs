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
        private readonly Dictionary<string, ControllerActionMeta> _actionsDict;

        public InvokeActionsDictionary(Dictionary<string, Type> controllers)
        {
            // Методы типа "home/hello" без учета регистра.
            _actionsDict = new Dictionary<string, ControllerActionMeta>(StringComparer.OrdinalIgnoreCase)
            {
                { "/SignIn", new ControllerActionMeta("SignIn", typeof(AccountController), AccountController.SignInMethod) },
                { "/SignOut", new ControllerActionMeta("SignOut", typeof(AccountController), AccountController.SignOutMethod) }
            };

            foreach (KeyValuePair<string, Type> controller in controllers)
            {
                MethodInfo[] methods = controller.Value.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (MethodInfo method in methods)
                {
                    string actionFullName = $"{controller.Key}{GlobalVars.ControllerNameSplitter}{method.Name}";
                    _actionsDict.Add(actionFullName, new ControllerActionMeta(actionFullName, controller.Value, method));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAction(string actionFullName, out ControllerActionMeta value)
        {
            return _actionsDict.TryGetValue(actionFullName, out value);
        }
    }
}
