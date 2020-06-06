using System;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class Controller
    {
        // Должен быть пустой конструктор для наследников.
        public Controller()
        {

        }

        internal abstract void BeforeInvokeController(ManagedConnection connection, ClaimsPrincipal? user);

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
