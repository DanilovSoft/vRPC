using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        public ManagedConnection Context { get; }

        public JRequest(ManagedConnection context, IResponseAwaiter responseAwaiter, RequestMethodMeta requestMeta, object[] args, int uid)
        {
            Context = context;
            ResponseAwaiter = responseAwaiter;
            MethodMeta = requestMeta;
            Args = args;
            Uid = uid;
        }

        /// <summary>
        /// Сериализация пользовательских данных может спровоцировать исключение 
        /// <exception cref="VRpcSerializationException"/> которое будет перенаправлено ожидающему потоку.
        /// </summary>
        internal bool TrySerialize([NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer)
        {
            buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                JsonRpcSerializer.SerializeRequest(buffer, MethodMeta.FullName, Args, Uid);
                toDispose = null; // Предотвратить Dispose.
                return true;
            }
            catch (Exception ex)
            {
                var vex = new VRpcSerializationException("Ошибка при сериализации пользовательских данных.", ex);
                ResponseAwaiter.TrySetException(vex);
                return false;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }
    }
}
