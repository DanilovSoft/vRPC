﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace DanilovSoft.vRPC.JsonRpc.ActionResults
{
    internal sealed class JMethodNotFoundResult : IActionResult
    {
        public JMethodNotFoundResult()
        {
            
        }

        public void ExecuteResult(ref ActionContext context)
        {
            JsonRpcSerializer.SerializeErrorResponse(context.ResponseBuffer, StatusCode.MethodNotFound, "Method not found", context.Id);
        }
    }
}
