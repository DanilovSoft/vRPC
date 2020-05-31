using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public interface IActionResult
    {
        void ExecuteResult(ActionContext context);
    }

    //internal interface IInnerActionResult
    //{

    //}

    //public interface IActionResult<T> : IActionResult
    //{
        
    //}
}
