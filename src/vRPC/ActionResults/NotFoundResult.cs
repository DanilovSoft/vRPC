using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class NotFoundResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.MethodNotFound;
        private readonly string _message;

        public NotFoundResult(string message)
        {
            _message = message;
        }

        public void ExecuteResult(ref ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseBuffer.WriteStringBinary(_message);
        }
    }
}
