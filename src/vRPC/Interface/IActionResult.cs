using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public interface IActionResult
    {
        void WriteVRpcResult(ref ActionContext context);

        /// <exception cref="JsonException"/>
        internal void WriteJsonRpcResult(int? id, ArrayBufferWriter<byte> buffer);
        
        internal ArrayBufferWriter<byte> WriteJsonRpcResult(int? id);
    }
}
