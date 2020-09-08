using System.Buffers;
using System.Diagnostics;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Пустой результат с кодом Ok.
    /// </summary>
    public class OkResult : ActionResult
    {
        private const StatusCode DefaultStatusCode = StatusCode.Ok;

        public OkResult() : base(DefaultStatusCode)
        {

        }

        private protected override void FinalWriteJsonRpcResult(int id, IBufferWriter<byte> buffer)
        {
            Debug.Assert(false);

            // Записать "result": Null
            throw new System.NotImplementedException();
        }

        private protected sealed override void FinalWriteResult(ref ActionContext context)
        {
            context.StatusCode = StatusCode;
        }
    }
}