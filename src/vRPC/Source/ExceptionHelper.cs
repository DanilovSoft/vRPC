using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Source
{
    internal static class ExceptionHelper
    {
        private static readonly Dictionary<StatusCode, Func<string?, VRpcException>> _dict = new Dictionary<StatusCode, Func<string?, VRpcException>>
        {
            [StatusCode.MethodNotFound] = (msg) => new VRpcMethodNotFoundException(msg),
            [StatusCode.InvalidParams] = (msg) => new VRpcInvalidParamsException(msg)
        };

        static ExceptionHelper()
        {

        }

        /// <returns>
        /// <list type="bullet">
        /// <item><see cref="VRpcMethodNotFoundException"/></item>
        /// <item><see cref="VRpcInvalidParamsException"/></item>
        /// <item><see cref="VRpcUnknownErrorException"/></item>
        /// </list>
        /// </returns>
        public static VRpcException ToException(StatusCode code, string? message)
        {
            if (_dict.TryGetValue(code, out var exception))
            {
                return exception(message);
            }
            else
            // Неизвестная ошибка.
            {
                return new VRpcUnknownErrorException(code, message);
            }
        }
    }
}
