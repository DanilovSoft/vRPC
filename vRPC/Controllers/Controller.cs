using System;

namespace DanilovSoft.vRPC
{
    public abstract class Controller /*: IDisposable*/
    {
        public Controller()
        {

        }

        //public virtual void Dispose()
        //{
            
        //}

        protected BadRequestResult BadRequest(string message)
        {
            return new BadRequestResult(message);
        }

        protected BadRequestResult<T> BadRequest<T>(string message)
        {
            return new BadRequestResult<T>(message);
        }

        protected OkResult Ok()
        {
            return new OkResult();
        }

        protected OkObjectResult<T> Ok<T>(T result)
        {
            return new OkObjectResult<T>(result);
        }

        protected OkObjectResult Ok(object result)
        {
            return new OkObjectResult(result);
        }
    }
}
