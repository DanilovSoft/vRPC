using System;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class Controller
    {
        private RequestContext? _requestContext;
        public bool IsNotification => !_requestContext!.IsResponseRequired;

        // Должен быть пустой конструктор для наследников.
        public Controller()
        {

        }

        internal abstract void BeforeInvokeController(ManagedConnection connection, ClaimsPrincipal? user);

        internal void BeforeInvokeController(RequestContext requestContext)
        {
            _requestContext = requestContext;
        }

        protected static BadRequestResult BadRequest(string errorMessage)
        {
            return new BadRequestResult(errorMessage);
        }

        protected static OkResult Ok()
        {
            return new OkResult();
        }

        protected static OkObjectResult Ok(object value)
        {
            return new OkObjectResult(value);
        }
    }
}
