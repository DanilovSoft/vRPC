using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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

        public void WriteResult(ref ActionContext context)
        {
            ActionResult result = Convert();
            result.WriteResult(ref context);
        }

        public void WriteJsonRpcResult(int id, IBufferWriter<byte> buffer)
        {
            ActionResult result = Convert();
            result.WriteJsonRpcResult(id, buffer);
        }
    }
}
