using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
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
        public RequestMessageDto ReceivedRequest { get; }

        public RequestContext(RequestMessageDto receivedRequest)
        {
            ReceivedRequest = receivedRequest;
        }
    }
}
