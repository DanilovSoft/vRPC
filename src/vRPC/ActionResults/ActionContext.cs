using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Контекст запроса.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    public struct ActionContext
    {
        /// <summary>
        /// Идентификатор запроса.
        /// </summary>
        internal int Id { get; }
        /// <summary>
        /// Буфер арендованной памяти для записи ответа.
        /// </summary>
        internal ArrayBufferWriter<byte> ResponseBuffer { get; }
        /// <summary>
        /// Может быть <see langword="null"/> если не удалось разобрать запрос.
        /// </summary>
        internal ControllerMethodMeta? Method { get; }
        public StatusCode StatusCode { get; internal set; }
        internal string? ProducesEncoding { get; set; }

        internal ActionContext(int id, ControllerMethodMeta? method, ArrayBufferWriter<byte> responseBuffer)
        {
            Id = id;
            Method = method;
            ResponseBuffer = responseBuffer;
            StatusCode = StatusCode.None;
            ProducesEncoding = null;
        }
    }
}