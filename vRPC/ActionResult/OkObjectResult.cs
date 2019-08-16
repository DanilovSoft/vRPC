namespace vRPC
{
    public sealed class OkObjectResult : ObjectResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.Ok;

        public OkObjectResult(object value) : base(value, DefaultStatusCode)
        {
            
        }
    }
}