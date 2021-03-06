﻿using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Код ошибки -32603, аналогично 500.
    /// </summary>
    internal sealed class InternalErrorResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.InternalError;
        private readonly string _message;

        /// <summary>
        /// Код ошибки -32603, аналогично 500.
        /// </summary>
        public InternalErrorResult(string message)
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
