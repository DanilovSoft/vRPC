using System;

namespace vRPC
{
    public abstract class Controller : IDisposable
    {
        public Controller()
        {

        }

        public virtual void Dispose()
        {
            
        }

        protected BadRequestResult BadRequest(string message)
        {
            return new BadRequestResult(message);
        }

        protected OkResult Ok()
        {
            return new OkResult();
        }

        protected OkObjectResult Ok(object result)
        {
            return new OkObjectResult(result);
        }
    }
}
