using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC
{
    internal interface IJRequest
    {
        RequestMethodMeta Method { get; }
        object[]? Args { get; }
        bool TrySerialize(out ArrayBufferWriter<byte> buffer);
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
