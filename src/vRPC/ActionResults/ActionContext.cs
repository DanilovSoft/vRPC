﻿using System.Runtime.InteropServices;

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
        internal int? Id { get; }
        /// <summary>
        /// Буфер арендованной памяти для записи ответа.
        /// </summary>
        internal ArrayBufferWriter<byte> ResponseBuffer { get; }
        /// <summary>
        /// Может быть <see langword="null"/> если не удалось разобрать запрос.
        /// </summary>
        internal ControllerMethodMeta? Method { get; }
        internal StatusCode StatusCode { get; set; }
        internal string? ProducesEncoding { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="method">Может быть Null если не удалось разобрать запрос.</param>
        /// <param name="responseBuffer"></param>
        internal ActionContext(int? id, ControllerMethodMeta? method, ArrayBufferWriter<byte> responseBuffer)
        {
            Id = id;
            Method = method;
            ResponseBuffer = responseBuffer;
            StatusCode = StatusCode.None;
            ProducesEncoding = null;
        }
    }
}