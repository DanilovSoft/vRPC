namespace DanilovSoft.vRPC
{
    public class OkObjectResult : ObjectResult
    {
        public OkObjectResult(object? value) : base(value)
        {
            StatusCode = StatusCode.Ok;
        }
    }
}