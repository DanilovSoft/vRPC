using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC
{
    internal interface IVRequest
    {
        RequestMethodMeta Method { get; }
        object[]? Args { get; }
        bool TrySerialize(out ArrayBufferWriter<byte> buffer, out int headerSize);
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
