using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class NotFoundResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.ActionNotFound;
        private readonly string _message;

        public NotFoundResult(string message)
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
