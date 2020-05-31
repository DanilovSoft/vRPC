using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public class StatusCodeResult : ActionResult
    {
        public StatusCodeResult(StatusCode statusCode) : base(statusCode)
        {
        }
    }
}
