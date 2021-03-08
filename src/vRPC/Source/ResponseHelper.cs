using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace DanilovSoft.vRPC.Source
{
    internal static class ResponseHelper
    {
        private const string ArgumentsCountMismatch = "Argument count mismatch for method '{0}'. {1} arguments was expected.";

        internal static MethodNotFoundResult MethodNotFound(string methodName)
        {
            int controllerIndex = methodName.IndexOf(GlobalVars.ControllerNameSplitter, StringComparison.Ordinal);

            if (controllerIndex > 0)
            {
                return new MethodNotFoundResult(methodName, $"Method \"{methodName}\" not found.");
            }
            else
            {
                return new MethodNotFoundResult(methodName, $"Controller name not specified in request \"{methodName}\".");
            }
        }

        internal static InvalidRequestResult ErrorDeserializingArgument(string actionName, short argIndex, Type argType)
        {
            if (argType.IsClrType())
            {
                return new InvalidRequestResult($"Не удалось десериализовать аргумент №{argIndex} в тип {argType.Name} метода {actionName}");
            }
            else
            // Не будем раскрывать удалённой стороне имена сложных типов.
            {
                return new InvalidRequestResult($"Не удалось десериализовать аргумент №{argIndex} метода {actionName}");
            }
        }

        internal static InvalidParamsResult JErrorDeserializingArgument(string actionName, short argIndex, Type argType)
        {
            if (argType.IsClrType())
            {
                return new InvalidParamsResult($"Не удалось десериализовать аргумент №{argIndex} в тип {argType.Name} метода {actionName}");
            }
            else
            // Не будем раскрывать удалённой стороне имена сложных типов.
            {
                return new InvalidParamsResult($"Не удалось десериализовать аргумент №{argIndex} метода {actionName}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static InvalidParamsResult ArgumentsCountMismatchError(string actionName, int targetArgumentsCount)
        {
            return new InvalidParamsResult(string.Format(CultureInfo.InvariantCulture, ArgumentsCountMismatch, actionName, targetArgumentsCount));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static InvalidParamsResult JArgumentsCountMismatchError(string actionName, int targetArgumentsCount)
        {
            return new InvalidParamsResult(string.Format(CultureInfo.InvariantCulture, ArgumentsCountMismatch, actionName, targetArgumentsCount));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ValidateArgumentsCount(ParameterInfo[] targetArguments, short jsonArgsCount, string actionName, 
            [NotNullWhen(false)] out IActionResult? error)
        {
            if (jsonArgsCount == targetArguments.Length)
            {
                error = null;
                return true;
            }
            else
            {
                error = ArgumentsCountMismatchError(actionName, targetArguments.Length);
                return false;
            }
        }
    }
}
