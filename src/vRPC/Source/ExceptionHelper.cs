using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Source
{
    internal static class ExceptionHelper
    {
        public static VRpcException ToException(StatusCode code, string? message)
        {
            throw new NotImplementedException();
            switch (code)
            {
                case StatusCode.None:
                    break;
                case StatusCode.Ok:
                    break;
                case StatusCode.Request:
                    break;
                case StatusCode.BadRequest:
                    break;
                case StatusCode.Unauthorized:
                    break;
                case StatusCode.ParseError:
                    break;
                case StatusCode.InvalidRequest:
                    break;
                case StatusCode.MethodNotFound:
                    break;
                case StatusCode.InvalidParams:
                    break;
                case StatusCode.InternalError:
                    break;
                default:
                    break;
            }
        }
    }
}
