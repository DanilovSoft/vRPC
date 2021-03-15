using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace DanilovSoft.vRPC
{
    public class ObjectResult : ActionResult
    {
        public object? Value { get; }
        public Type? DeclaredType { get; set; }

        public ObjectResult(object? value) : base(StatusCode.Ok)
        {
            Value = value;
        }

        private protected sealed override void FinalWriteResult(ref ActionContext context)
        {
            context.StatusCode = StatusCode;

            // Нет необходимости отправлять Null.
            if (Value != null)
            {
                Debug.Assert(context.Method != null, "ObjectResult можно получить только из контекста метода");

                // Сериализуем в стрим.
                context.Method.SerializerDelegate(context.ResponseBuffer, Value);

                // Устанавливаем формат.
                context.ProducesEncoding = context.Method.ProducesEncoding;
            }
        }

        private protected override void FinalWriteJsonRpcResult(int? id, IBufferWriter<byte> buffer)
        {
            // Сериализуем ответ.
            JsonRpcSerializer.SerializeResponse(buffer, id.Value, Value);

            //JsonRpcSerializer.SerializeErrorResponse(buffer, DefaultStatusCode, _message, id);
        }
    }
}
