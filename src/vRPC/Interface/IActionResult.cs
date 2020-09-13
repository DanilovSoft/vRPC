using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public interface IActionResult
    {
        void WriteVRpcResult(ref ActionContext context);
        void WriteJsonRpcResult(int? id, IBufferWriter<byte> buffer);
    }
}
