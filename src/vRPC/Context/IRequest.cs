using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    internal interface IRequest : IMessageToSend
    {
        RequestMethodMeta? Method { get; }
        object[]? Args { get; }
        bool IsNotification { get; }
        /// <summary>
        /// Переводит сообщение в состояние отправки, что-бы другие потоки не вмешивались.
        /// </summary>
        bool TryBeginSend();
        //void EndSend();
        /// <summary>
        /// Если запрос является нотификацией то завершает его ожидание отправки.
        /// </summary>
        void CompleteSend(VRpcException exception);
        /// <summary>
        /// Если запрос является нотификацией то завершает его ожидание отправки.
        /// </summary>
        void CompleteSend();
    }
}
