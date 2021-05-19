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
        
        object?[]? Args { get; }
        
        bool IsNotification { get; }
        
        /// <summary>
        /// Сообщение мог завершить другой поток, поэтому пробуем атомарно получить доступ.
        /// </summary>
        /// <remarks>Переводит сообщение в состояние отправки.</remarks>
        bool TryBeginSend();

        /// <summary>
        /// Если запрос является нотификацией то завершает его ожидание отправки.
        /// </summary>
        void CompleteSend(VRpcException exception);
        
        /// <summary>
        /// Уведомляет что работа с общим буфером завершена.
        /// </summary>
        void CompleteSend();
    }
}
