namespace DanilovSoft.vRPC
{
    public class OkObjectResult : ObjectResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.Ok;

        public OkObjectResult(object value) : base(value, DefaultStatusCode)
        {
            
        }
    }

    public sealed class OkObjectResult<T> : OkObjectResult, IActionResult<T>
    {
        public OkObjectResult(T result) : base(result)
        {

        }
    }
}