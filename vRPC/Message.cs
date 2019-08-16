using System.Diagnostics;

namespace vRPC
{
    /// <summary>
    /// Сериализуемое сообщение для удаленного соединения.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    internal sealed class Message
    {
        #region Debug

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"\"{(IsRequest ? $"Request: {ActionName}" : $"Result: {Result}")}\"" + "}";

        #endregion

        public short Uid { get; set; }
        public bool IsRequest { get; }
        //public StatusCode StatusCode { get; private set; }
        public string ActionName { get; private set; }
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public Arg[] Args { get; private set; }
        public object Result { get; private set; }

        //public string Error { get; private set; }

        /// <summary>
        /// Связанный запрос. Может быть <see langword="null"/>.
        /// </summary>
        public RequestMessage ReceivedRequest { get; set; }

        /// <summary>
        /// Конструктор запроса.
        /// </summary>
        private Message(string actionName, Arg[] args)
        {
            ActionName = actionName;
            IsRequest = true;
            //StatusCode = StatusCode.Request;
            Args = args;
        }

        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        private Message(short uid, object result)
        {
            Uid = uid;
            Result = result;
            //StatusCode = errorCode;
        }

        public static Message CreateRequest(string actionName, Arg[] args)
        {
            return new Message(actionName, args);
        }

        public static Message FromResult(short uid, object rawResult)
        {
            return new Message(uid, rawResult);
        }

        public static Message FromResult(RequestMessage receivedRequest, object rawResult)
        {
            var message = new Message(receivedRequest.Header.Uid, rawResult);

            message.ReceivedRequest = receivedRequest;
            return message;
        }

        //public static Message FromError(short uid, RemoteException remoteException)
        //{
        //    return new Message(uid, result: new ErrorResult(remoteException.Message), remoteException.ErrorCode);
        //}

        //public static Message FromError(short uid, string errorMessage, StatusCode errorCode)
        //{
        //    return new Message(uid, result: new ErrorResult(errorMessage), errorCode);
        //}
    }
}
