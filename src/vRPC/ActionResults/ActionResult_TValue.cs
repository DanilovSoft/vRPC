using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC
{
    public sealed class ActionResult<TValue> : IActionResult
    {
        [AllowNull]
        public TValue Value { get; }
        public ActionResult? Result { get; }

        public ActionResult(ActionResult result)
        {
            if (result == null)
                ThrowHelper.ThrowArgumentNullException(nameof(result));

            Result = result;
            Value = default;
        }

        public ActionResult(TValue value)
        {
            Value = value;
        }

        public static implicit operator ActionResult<TValue>(TValue result)
        {
            return new ActionResult<TValue>(result);
        }

        public static implicit operator ActionResult<TValue>(ActionResult result)
        {
            return new ActionResult<TValue>(result);
        }

        private ActionResult Convert()
        {
            return Result ?? new ObjectResult(Value)
            {
                DeclaredType = typeof(TValue),
            };
        }

        public void WriteVRpcResult(ref ActionContext context)
        {
            ActionResult result = Convert();
            result.WriteVRpcResult(ref context);
        }

        void IActionResult.WriteJsonRpcResult(int? id, ArrayBufferWriter<byte> buffer)
        {
            ActionResult result = Convert();
            result.InnerWriteJsonRpcResult(id, buffer);
        }

        ArrayBufferWriter<byte> IActionResult.WriteJsonRpcResult(int? id)
        {
            Debug.Assert(false);
            throw new NotImplementedException();
        }
    }
}
