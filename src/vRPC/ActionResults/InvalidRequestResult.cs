using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{InvalidRequestResult: {_message}\}")]
    internal sealed class InvalidRequestResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InvalidRequest;
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
