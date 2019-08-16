using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    internal sealed class InvalidRequestResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InvalidRequestFormat;
        private readonly string _message;

        public InvalidRequestResult(string message)
        {
            _message = message;
        }

        public void ExecuteResult(ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseStream.WriteStringBinary(_message);
        }
    }
}
