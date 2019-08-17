﻿using System.Diagnostics;

namespace vRPC
{
    /// <summary>
    /// Сериализуемое сообщение для передачи удаленной стороне.
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
        public string ActionName { get; private set; }
        /// <summary>
        /// Параметры для удаленного метода <see cref="ActionName"/>.
        /// </summary>
        public Arg[] Args { get; private set; }
        public object Result { get; private set; }
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
            Args = args;
        }

        /// <summary>
        /// Конструктор ответа.
        /// </summary>
        private Message(short uid, object result)
        {
            Uid = uid;
            Result = result;
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
    }
}