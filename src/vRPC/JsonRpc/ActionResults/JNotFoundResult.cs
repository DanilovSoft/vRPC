using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc.ActionResults
{
    internal sealed class JNotFoundResult : IActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.MethodNotFound;

        public JNotFoundResult(string method)
        {

        }

        public void ExecuteResult(ActionContext context)
        {
            throw new NotImplementedException();
        }
    }
}
