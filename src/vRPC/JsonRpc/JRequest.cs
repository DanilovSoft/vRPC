using System;
using System.Collections.Generic;
using System.Text;

namespace DanilovSoft.vRPC.Context
{
    internal sealed class JRequest : IMessageToSend
    {
        internal readonly RequestMethodMeta MethodMeta;
        internal readonly object[] Args;

        /// <summary>
        /// Уникальный идентификатор который будет отправлен удалённой стороне.
        /// </summary>
        internal readonly int Uid;
        internal readonly IResponseAwaiter ResponseAwaiter;

        public JRequest(IResponseAwaiter responseAwaiter, RequestMethodMeta requestMeta, object[] args, int uid)
        {
            ResponseAwaiter = responseAwaiter;
            MethodMeta = requestMeta;
            Args = args;
            Uid = uid;
        }

        /// <summary>
        /// Сериализация пользовательских данных может спровоцировать исключение.
        /// </summary>
        /// <exception cref="VRpcSerializationException"/>
        /// <returns></returns>
        internal ArrayBufferWriter<byte> Serialize()
        {
            // Арендуем заранее под максимальный размер хэдера.
            var buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                JsonRpcSerializer.SerializeRequest(buffer, MethodMeta.MethodFullName, Args, Uid);
                toDispose = null; // Предотвратить Dispose.
                return buffer;
            }
            catch (Exception ex)
            {
                throw new VRpcSerializationException("Ошибка при сериализации пользовательских данных.", ex);
            }
            finally
            {
                toDispose?.Dispose();
            }
        }
    }
}
