using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит запрос полученный от удалённой стороны.
    /// </summary>
    internal sealed class RequestContext
    {
        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerAction ActionToInvoke { get; internal set; }
        /// <summary>
        /// Десериализованный заголовок запроса. Не может быть <see langword="null"/>.
        /// </summary>
        public HeaderDto HeaderDto { get; }
        /// <summary>
        /// Десериализованный запрос. Не может быть <see langword="null"/>.
        /// </summary>
        public RequestMessageDto RequestDto { get; }

        public RequestContext(in HeaderDto header, RequestMessageDto receivedRequest)
        {
            HeaderDto = header;
            RequestDto = receivedRequest;
        }
    }
}
