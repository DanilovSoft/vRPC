using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public abstract class ActionResult : IActionResult
    {
        public StatusCode StatusCode { get; internal set; }

        public ActionResult(StatusCode statusCode)
        {
            StatusCode = statusCode;
        }

        public virtual Task ExecuteResultAsync(ref ActionContext context)
        {
            ExecuteResult(ref context);
            return Task.CompletedTask;
        }

        public virtual void ExecuteResult(ref ActionContext context)
        {
            FinalExecuteResult(ref context);
        }

        private protected virtual void FinalExecuteResult(ref ActionContext context)
        {

        }
    }
}
