using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc.ActionResults
{
    internal sealed class JInvalidRequestResult : IJActionResult
    {
        public const StatusCode DefaultStatusCode = StatusCode.InvalidRequest;

        public void ExecuteResult(ref ActionContext context)
        {
            Debug.Assert(false);
            JsonRpcSerializer.SerializeErrorResponse(context.ResponseBuffer, DefaultStatusCode, "Invalid Request", context.Id);
        }
    }
}
