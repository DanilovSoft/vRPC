using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface IRequest : IMessageToSend
    {
        RequestMethodMeta Method { get; }
        object[]? Args { get; }
        bool IsNotification { get; }
        /// <summary>
        /// Если запрос является нотификацией то завершает его ожидание отправки.
        /// </summary>
        void CompleteNotification(VRpcException exception);
        /// <summary>
        /// Если запрос является нотификацией то завершает его ожидание отправки.
        /// </summary>
        void CompleteNotification();
    }
}
