using System;
using System.Collections.Generic;
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

        private protected override void FinalExecuteResult(ActionContext context)
        {
            context.StatusCode = StatusCode;

            // Нет необходимости отправлять Null.
            if (Value != null)
            {
                // Сериализуем в стрим.
                context.RequestContext.ActionToInvoke.SerializerDelegate(context.ResponseStream, Value);

                // Устанавливаем формат.
                context.ProducesEncoding = context.RequestContext.ActionToInvoke.ProducesEncoding;
            }
        }
    }
}
