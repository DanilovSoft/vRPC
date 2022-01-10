using System.Text.Json;

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
