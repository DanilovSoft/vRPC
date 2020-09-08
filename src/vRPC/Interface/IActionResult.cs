using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    //internal interface IJActionResult : IActionResult
    //{
        
    //}

    public interface IActionResult
    {
        void WriteResult(ref ActionContext context);
        void WriteJsonRpcResult(int id, IBufferWriter<byte> buffer);
    }
}
