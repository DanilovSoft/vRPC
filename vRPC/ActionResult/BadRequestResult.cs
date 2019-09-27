namespace DanilovSoft.vRPC
{
    public class BadRequestResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.BadRequest;
        private readonly string _message;

        public BadRequestResult(string message)
        {
            _message = message;
        }

        public void ExecuteResult(ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseStream.WriteStringBinary(_message);
        }
    }

    public sealed class BadRequestResult<T> : BadRequestResult, IActionResult<T>
    {
        public BadRequestResult(string message) : base(message)
        {
            
        }
    }
}