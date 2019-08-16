using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    public abstract class ActionResult : IActionResult
    {
        public StatusCode StatusCode { get; }

        public ActionResult(StatusCode statusCode)
        {
            StatusCode = statusCode;
        }

        public virtual Task ExecuteResultAsync(ActionContext context)
        {
            ExecuteResult(context);
            return Task.CompletedTask;
        }

        public virtual void ExecuteResult(ActionContext context)
        {

        }
    }
}
