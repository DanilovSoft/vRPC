using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace vRPC
{
    public interface IActionResult
    {
        void ExecuteResult(ActionContext context);
    }
}
