using DanilovSoft.vRPC.Context;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит запрос с параметрами полученный от удалённой стороны который необходимо выполнить.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay(@"\{{" + nameof(Method) + @" ?? default}\}")]
    internal sealed class RequestContext : IThreadPoolWorkItem, IMessageToSend, IDisposable
    {
        /// <summary>
        /// Когда Id не Null.
        /// </summary>
        public bool IsResponseRequired => Id != null;
        public int? Id { get; }

        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerMethodMeta Method { get; }

        /// <summary>
        /// Аргументы для вызываемого метода.
        /// </summary>
        public object[] Args { get; }

        public ManagedConnection Context { get; }
        /// <summary>
        /// Если запрос получен в формате JSON-RPC, то и ответ должен быть в формате JSON-RPC.
        /// </summary>
        public bool IsJsonRpcRequest { get; }

        internal object? Result { get; set; }
        internal StatusCode ResultCode { get; private set; }
        internal string? ResultEncoding { get; private set; }

        // ctor
        public RequestContext(ManagedConnection connection, int? id, ControllerMethodMeta method, object[] args, bool isJsonRpc)
        {
            Debug.Assert(method != null);

            Context = connection;
            Id = id;
            Method = method;
            Args = args;
            IsJsonRpcRequest = isJsonRpc;
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        internal ArrayBufferWriter<byte> SerializeResponseAsVrpc(out int headerSize)
        {
            Debug.Assert(!IsJsonRpcRequest, "Формат ответа и запроса не совпадают");

            var buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                if (Result is IActionResult actionResult)
                // Метод контроллера вернул специальный тип.
                {
                    var actionContext = new ActionContext(Id, Method, buffer);

                    // Сериализуем ответ.
                    actionResult.WriteVRpcResult(ref actionContext);
                    ResultCode = actionContext.StatusCode;
                    ResultEncoding = actionContext.ProducesEncoding;
                }
                else
                // Отправлять результат контроллера будем как есть.
                {
                    // Сериализуем ответ.
                    ResultCode = StatusCode.Ok;

                    // Сериализуем контент если он есть (у void его нет).
                    if (Result != null)
                    {
                        Debug.Assert(Method != null, "RAW результат может быть только на основе запроса");
                        Method.SerializerDelegate(buffer, Result);
                        ResultEncoding = Method.ProducesEncoding;
                    }
                }

                headerSize = AppendHeader(buffer);

                toDispose = null;
                return buffer;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        internal ArrayBufferWriter<byte> SerializeResponseAsJrpc()
        {
            Debug.Assert(IsJsonRpcRequest, "Формат ответа и запроса не совпадают");
            Debug.Assert(Id != null);

            var buffer = new ArrayBufferWriter<byte>();
            var toDispose = buffer;
            try
            {
                if (Result is IActionResult actionResult)
                // Метод контроллера вернул специальный тип.
                {
                    // Сериализуем ответ.
                    actionResult.WriteJsonRpcResult(Id.Value, buffer);
                }
                else
                // Отправлять результат контроллера будем как есть.
                {
                    Debug.Assert(false);
                    throw new NotImplementedException();
                    // Сериализуем ответ.
                    //serMsg.StatusCode = StatusCode.Ok;

                    //    Debug.Assert(jResponse.ActionMeta != null, "RAW результат может быть только на основе запроса");
                    //    jResponse.ActionMeta.SerializerDelegate(serMsg.MemoryPoolBuffer, responseToSend.ActionResult);
                    //    serMsg.ContentEncoding = jResponse.ActionMeta.ProducesEncoding;
                }
                toDispose = null; // Предотвратить Dispose.
                return buffer;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        /// <summary>
        /// Сериализует хэдер в стрим сообщения.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private int AppendHeader(ArrayBufferWriter<byte> buffer)
        {
            Debug.Assert(Id != null);

            HeaderDto header = new HeaderDto(Id.Value, buffer.WrittenCount, ResultEncoding, responseCode: ResultCode);

            // Записать заголовок в конец стрима. Не бросает исключения.
            int headerSize = header.SerializeJson(buffer);

            // Запомним размер хэдера.
            return headerSize;
        }

        //private HeaderDto CreateHeader(int payloadLength)
        //{
        //    if (Result is ResponseMessage responseToSend)
        //    // Создать хедер ответа на запрос.
        //    {
        //        //Debug.Assert(ResultCode != null, "StatusCode ответа не может быть Null");

        //        return new HeaderDto(Id.Value, payloadLength, ResultEncoding, responseCode: ResultCode);
        //    }
        //    else
        //    // Создать хедер для нового запроса.
        //    {
        //        var request = messageToSend.MessageToSend as RequestMethodMeta;
        //        Debug.Assert(request != null);

        //        return new HeaderDto(messageToSend.Uid, messageToSend.ContentEncoding, request.FullName, messageToSend.Buffer.WrittenCount);
        //    }
        //}

        /// <summary>
        /// В новом потоке выполняет запрос и отправляет обратно результат или ошибку.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        internal void StartProcessRequest()
        {
#if NETSTANDARD2_0 || NET472
            ThreadPool.UnsafeQueueUserWorkItem(ProcessRequestThreadEntryPoint, state: this); // Без замыкания.
            
            static void ProcessRequestThreadEntryPoint(object? state)
            {
                var request = (RequestContext)state!;
                request.Context.OnStartProcessRequest(request);
            }  
#else
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: true);
#endif
        }

        // Вызывается пулом потоков.
        public void Execute()
        {
            Context.OnStartProcessRequest(this);
        }

        public void Dispose()
        {
            Method.DisposeArgs(Args);
        }
    }
}
