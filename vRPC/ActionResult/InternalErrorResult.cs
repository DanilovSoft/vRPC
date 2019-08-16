﻿using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    internal sealed class InternalErrorResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InternalError;
        private readonly string _message;

        public InternalErrorResult(string message)
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
