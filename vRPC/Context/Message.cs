using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace vRPC
{
    /// <summary>
    /// Сообщение для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal abstract class Message
    {
        #region Debug

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"\"{(this is RequestMessage ? $"Request: {ActionName}" : $"Result: {Result}")}\"" + "}";

        #endregion

        public ushort Uid { get; set; }
        //public MessageType MessageType { get; }
        public string ActionName { get; protected set; }
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public JToken[] Args { get; protected set; }
        public object Result { get; protected set; }
        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/>.
        /// </summary>
        public RequestMessageDto ReceivedRequest { get; set; }

        protected Message()
        {

        }

        public static Arg[] PrepareArgs(object[] args)
        {
            var jArgs = new Arg[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                jArgs[i] = new Arg(args[i]);
            }
            return jArgs;
        }

        //public static Message CreateRequest(string actionName, Arg[] args)
        //{
        //    return new Message(actionName, args);
        //}

        //public static Message CreateResponse(ushort uid, object rawResult)
        //{
        //    return new Message(uid, rawResult);
        //}

        //public static Message ResponseFromResult(RequestMessageDto receivedRequest, object rawResult)
        //{
        //    var message = new Message(receivedRequest.Header.Uid, rawResult);
        //    message.ReceivedRequest = receivedRequest;
        //    return message;
        //}
    }
}
