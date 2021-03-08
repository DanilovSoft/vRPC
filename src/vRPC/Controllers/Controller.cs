using System;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class Controller
    {
        public bool IsNotification { get; private set; }

        // Должен быть пустой конструктор для наследников.
        public Controller()
        {

        }

        internal abstract void BeforeInvokeController(VrpcManagedConnection connection, ClaimsPrincipal? user);

        internal void BeforeInvokeController(in RequestContext requestContext)
        {
            IsNotification = !requestContext.IsResponseRequired;
        }

        protected static InvalidParamsResult InvalidParams(string errorMessage)
        {
            return new InvalidParamsResult(errorMessage);
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
