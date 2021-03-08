using DanilovSoft.vRPC.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay("Count = {_actionsDict.Count}")]
    internal sealed class InvokeActionsDictionary
    {
        /// <summary>
        /// Словарь используемый только для чтения.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private readonly Dictionary<string, ControllerMethodMeta> _actionsDict;

        public InvokeActionsDictionary(Dictionary<string, Type> controllers)
        {
            // Методы типа "home/hello" без учета регистра.
            _actionsDict = new Dictionary<string, ControllerMethodMeta>(StringComparer.OrdinalIgnoreCase)
            {
                { "/SignIn", new ControllerMethodMeta("SignIn", typeof(AccountController), AccountController.SignInMethod) },
                { "/SignOut", new ControllerMethodMeta("SignOut", typeof(AccountController), AccountController.SignOutMethod) }
            };

            foreach (KeyValuePair<string, Type> controller in controllers)
            {
                MethodInfo[] methods = controller.Value.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (MethodInfo method in methods)
                {
                    string actionFullName = $"{controller.Key}{GlobalVars.ControllerNameSplitter}{method.Name}";
                    if (!_actionsDict.TryAdd(actionFullName, new ControllerMethodMeta(actionFullName, controller.Value, method)))
                    {
                        ThrowHelper.ThrowVRpcException($"Контроллер {controller.Value.Name} содержит несколько методов с одинаковым именем '{method.Name}'." +
                            $" Переименуйте методы так что-бы их имена были уникальны в пределах контроллера.");
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAction(string actionFullName, [MaybeNullWhen(false)] out ControllerMethodMeta value)
        {
            return _actionsDict.TryGetValue(actionFullName, out value);
        }
    }
}
