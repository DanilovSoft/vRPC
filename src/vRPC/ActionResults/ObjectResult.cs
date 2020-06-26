using System;
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

        private protected sealed override void FinalExecuteResult(ActionContext context)
        {
            context.StatusCode = StatusCode;

            // Нет необходимости отправлять Null.
            if (Value != null)
            {
                Debug.Assert(context.ActionMeta != null, "ObjectResult можно получить только из контекста метода");

                // Сериализуем в стрим.
                context.ActionMeta.SerializerDelegate(context.ResponseStream, Value);

                // Устанавливаем формат.
                context.ProducesEncoding = context.ActionMeta.ProducesEncoding;
            }
        }
    }
}
