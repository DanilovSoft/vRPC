using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    public class ObjectResult : ActionResult
    {
        private readonly object _value;

        public ObjectResult(object value, StatusCode statusCode) : base(statusCode)
        {
            _value = value;
        }

        public override void ExecuteResult(ActionContext context)
        {
            context.StatusCode = StatusCode;

            // Сериализуем в стрим.
            context.RequestContext.ActionToInvoke.SerializeObject(context.ResponseStream, _value);

            // Устанавливаем формат.
            context.ProducesEncoding = context.RequestContext.ActionToInvoke.ProducesEncoding;
        }
    }
}
