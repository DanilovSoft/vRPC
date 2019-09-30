using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    public class ObjectResult : ActionResult
    {
        private readonly object _value;

        public ObjectResult(object value, StatusCode statusCode) : base(statusCode)
        {
            _value = value;
        }

        private protected override void InnerExecuteResult(ActionContext context)
        {
            context.StatusCode = StatusCode;

            // Сериализуем в стрим.
            context.RequestContext.ActionToInvoke.Serializer(context.ResponseStream, _value);

            // Устанавливаем формат.
            context.ProducesEncoding = context.RequestContext.ActionToInvoke.ProducesEncoding;
        }
    }
}
