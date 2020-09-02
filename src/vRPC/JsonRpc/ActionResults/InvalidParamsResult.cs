using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DanilovSoft.vRPC.JsonRpc.ActionResults
{
    internal sealed class InvalidParamsResult : IActionResult
    {
        public InvalidParamsResult(string message)
        {

        }

        public void ExecuteResult(ref ActionContext context)
        {
            throw new NotImplementedException();
        }
    }
}
