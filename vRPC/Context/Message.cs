using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace vRPC
{
    /// <summary>
    /// Сообщение для передачи удаленной стороне. Не подлежит сериализации.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal sealed class Message
    {
        #region Debug

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"\"{(IsRequest ? $"Request: {ActionName}" : $"Result: {Result}")}\"" + "}";

        #endregion

        public ushort Uid { get; set; }
        public bool IsRequest { get; }
        public string ActionName { get; private set; }
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public JToken[] Args { get; private set; }
        public object Result { get; private set; }
        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/>.
        /// </summary>
        public RequestMessageDto ReceivedRequest { get; set; }

        /// <summary>
        /// Конструктор запроса.
        /// </summary>
        private Message(string actionName, Arg[] args)
        {
            ActionName = actionName;
            IsRequest = true;
            Args = new JToken[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Args[i] = args[i].Value;
            }
        }

        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        private Message(ushort uid, object result)
        {
            Uid = uid;
            Result = result;
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

        public static Message CreateRequest(string actionName, Arg[] args)
        {
            return new Message(actionName, args);
        }

        public static Message FromResult(ushort uid, object rawResult)
        {
            return new Message(uid, rawResult);
        }

        public static Message FromResult(RequestMessageDto receivedRequest, object rawResult)
        {
            var message = new Message(receivedRequest.Header.Uid, rawResult);

            message.ReceivedRequest = receivedRequest;
            return message;
        }
    }
}
