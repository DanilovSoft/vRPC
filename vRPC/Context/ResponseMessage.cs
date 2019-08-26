using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace vRPC
{
    internal sealed class ResponseMessage : Message
    {
        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        public ResponseMessage(ushort uid, object result)
        {
            Uid = uid;
            Result = result;
        }

        public ResponseMessage(RequestMessageDto receivedRequest, object rawResult) 
            : this(receivedRequest.Header.Uid, rawResult)
        {
            ReceivedRequest = receivedRequest;
        }
    }
}
