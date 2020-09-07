using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface IJActionResult : IActionResult
    {
        
    }

    public interface IActionResult
    {
        void ExecuteResult(ref ActionContext context);
    }
}
