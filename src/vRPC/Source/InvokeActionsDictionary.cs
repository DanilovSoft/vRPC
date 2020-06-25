using DanilovSoft.vRPC.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
                    if (!_actionsDict.TryAdd(actionFullName, new ControllerActionMeta(actionFullName, controller.Value, method)))
                    {
                        ThrowHelper.ThrowVRpcException($"Контроллер {controller.Value.Name} содержит несколько методов с одинаковым именем '{method.Name}'." +
                            $" Переименуйте методы так что-бы их имена были уникальны в пределах контроллера.");
                    }
                }
            }
        }

#if NETSTANDARD2_0 || NET472
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAction(string actionFullName, out ControllerActionMeta? value)
        {
            return _actionsDict.TryGetValue(actionFullName, out value);
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAction(string actionFullName, [MaybeNullWhen(false)] out ControllerActionMeta value)
        {
            return _actionsDict.TryGetValue(actionFullName, out value);
        }
#endif
    }
}
