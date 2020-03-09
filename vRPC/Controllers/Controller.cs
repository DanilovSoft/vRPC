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

        internal abstract void BeforeInvokeController(ManagedConnection connection, ClaimsPrincipal user);

        protected static BadRequestResult BadRequest(string message)
        {
            return new BadRequestResult(message);
        }

        protected static BadRequestResult<T> BadRequest<T>(string message)
        {
            return new BadRequestResult<T>(message);
        }

        protected static OkResult Ok()
        {
            return new OkResult();
        }

        protected static OkObjectResult<T> Ok<T>(T result)
        {
            return new OkObjectResult<T>(result);
        }

        protected static OkObjectResult Ok(object result)
        {
            return new OkObjectResult(result);
        }
    }
}
