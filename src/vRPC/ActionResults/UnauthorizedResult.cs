using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    internal sealed class UnauthorizedResult : IActionResult
    {
        private readonly string _message;
        private readonly StatusCode _statusCode;

        public UnauthorizedResult(string message, StatusCode statusCode)
        {
            _message = message;
            _statusCode = statusCode;
        }

        public void ExecuteResult(ref ActionContext context)
        {
            context.StatusCode = _statusCode;
            context.ResponseBuffer.WriteStringBinary(_message);
        }
    }
}
