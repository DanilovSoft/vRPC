using System;
using System.Diagnostics;

namespace DanilovSoft.vRPC
{
    [DebuggerDisplay(@"\{BadRequestResult: {_message}\}")]
    public class BadRequestResult : StatusCodeResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.BadRequest;
        private readonly string _message;

        public BadRequestResult(string message) : base (DefaultStatusCode)
        {
            _message = message;
        }

        private protected sealed override void FinalExecuteResult(ActionContext context)
        {
            context.StatusCode = DefaultStatusCode;
            context.ResponseStream.WriteStringBinary(_message);
        }
    }
}