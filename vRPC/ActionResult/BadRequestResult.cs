using System;

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

        void IActionResult.ExecuteResult(ActionContext context)
        {
            InnerExecuteResult(context);
        }

        private void InnerExecuteResult(ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseStream.WriteStringBinary(_message);
        }

        public void ExecuteResult(ActionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            InnerExecuteResult(context);
        }
    }

    public sealed class BadRequestResult<T> : BadRequestResult, IActionResult<T>
    {
        public BadRequestResult(string message) : base(message)
        {
            
        }
    }
}