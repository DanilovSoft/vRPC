﻿using DanilovSoft.vRPC.Context;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Содержит запрос с параметрами полученный от удалённой стороны который необходимо выполнить.
    /// </summary>
    //[StructLayout(LayoutKind.Auto)]
    //[DebuggerDisplay(@"\{{" + nameof(Method) + @" ?? default}\}")]
    internal sealed class RequestContext : IThreadPoolWorkItem, IMessageToSend
    {
        internal bool IsReusable { get; }
        /// <summary>
        /// Когда Id не Null.
        /// </summary>
        public bool IsResponseRequired => Id != null;
        /// <summary>
        /// Может быть Null если нотификация.
        /// </summary>
        public int? Id { get; private set; }
        /// <summary>
        /// Запрашиваемый метод контроллера.
        /// </summary>
        public ControllerMethodMeta? Method { get; private set; }
        /// <summary>
        /// Аргументы для вызываемого метода.
        /// </summary>
        public object?[]? Args { get; private set; }
        public RpcManagedConnection Connection { get; }
        /// <summary>
        /// Если запрос получен в формате JSON-RPC, то и ответ должен быть в формате JSON-RPC.
        /// </summary>
        public bool IsJsonRpcRequest { get; private set; }
        /// <summary>
        /// Результат вызова метода контроллера.
        /// </summary>
        internal object? Result { get; set; }

        public RequestContext(RpcManagedConnection context)
        {
            Connection = context;
            IsReusable = true;
        }

        // ctor
        public RequestContext(RpcManagedConnection connection, int? id, ControllerMethodMeta method, object?[] args, bool isJsonRpc)
        {
            IsReusable = false;
            Connection = connection;
            Initialize(id, method, args, isJsonRpc);
        }

        internal void Reset()
        {
            Id = null;
            Method = null;
            Args = null;
            IsJsonRpcRequest = false;
            Result = null;
        }

        internal void Initialize(int? id, ControllerMethodMeta method, object?[] args, bool isJsonRpc)
        {
            Debug.Assert(Id == null);
            Debug.Assert(Method == null);
            Debug.Assert(Args == null);
            Debug.Assert(IsJsonRpcRequest == default);

            Id = id;
            Method = method;
            Args = args;
            IsJsonRpcRequest = isJsonRpc;
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        /// <exception cref="JsonException">Ошибка сериализации пользовательских данных.</exception>
        /// <exception cref="ProtoBuf.ProtoException">Ошибка сериализации пользовательских данных.</exception>
        internal ArrayBufferWriter<byte> SerializeResponseAsVrpc(out int headerSize)
        {
            Debug.Assert(!IsJsonRpcRequest, "Формат ответа не совпадает с запросом");
            Debug.Assert(Id != null);

            var buffer = new ArrayBufferWriter<byte>();
            bool dispose = true;
            try
            {
                if (Result is IActionResult actionResult)
                // Метод контроллера вернул специальный тип.
                {
                    var actionContext = new ActionContext(Id, Method, buffer);

                    // Сериализуем ответ.
                    actionResult.WriteVRpcResult(ref actionContext);

                    headerSize = AppendHeader(buffer, Id.Value, actionContext.StatusCode, actionContext.ProducesEncoding);
                }
                else
                // Отправлять результат контроллера будем как есть.
                {
                    // Сериализуем контент если он есть (у void его нет).
                    if (Result != null)
                    {
                        Debug.Assert(Method != null, "RAW результат может быть только на основе запроса");

                        Method.SerializerDelegate(buffer, Result);

                        headerSize = AppendHeader(buffer, Id.Value, StatusCode.Ok, Method.ProducesEncoding);
                    }
                    else
                    // Null отправлять не нужно.
                    {
                        headerSize = AppendHeader(buffer, Id.Value, StatusCode.Ok, null);
                    }
                }
                dispose = false;
                return buffer;
            }
            finally
            {
                if (dispose)
                    buffer.Return();
            }
        }

        internal ArrayBufferWriter<byte> SerializeResponseAsJrpc()
        {
            Debug.Assert(IsJsonRpcRequest, "Формат ответа не совпадает с запросом");
            Debug.Assert(Id != null);

            var buffer = new ArrayBufferWriter<byte>();
            bool dispose = true;
            try
            {
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
                        // Сериализуем ответ.
                        JsonRpcSerializer.SerializeResponse(buffer, Id.Value, Result);
                    }
                }
                catch (JsonException)
                {
                    buffer.Clear();
                    var response = (IActionResult)new InternalErrorResult("Ошибка сериализации ответа");
                    response.WriteJsonRpcResult(Id.Value, buffer);
                }
                dispose = false; // Предотвратить Dispose.
                return buffer;
            }
            finally
            {
                if (dispose)
                    buffer.Return();
            }
        }

        /// <summary>
        /// Сериализует хэдер в стрим сообщения.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private static int AppendHeader(ArrayBufferWriter<byte> buffer, int id, StatusCode responseCode, string? encoding)
        {
            var header = new HeaderDto(id, buffer.WrittenCount, encoding, responseCode);

            // Записать заголовок в конец стрима. Не бросает исключения.
            int headerSize = header.SerializeJson(buffer);

            // Запомним размер хэдера.
            return headerSize;
        }

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
                request.Connection.OnStartProcessRequest(request);
            }  
#else
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: true);
#endif
        }

        // Вызывается пулом потоков.
        public void Execute()
        {
            Connection.OnStartProcessRequest(this);
        }

        public void DisposeArgs()
        {
            Debug.Assert(Args != null);
            Debug.Assert(Method != null);

            Method.DisposeArgs(Args);
            Args = null;
        }
    }
}
