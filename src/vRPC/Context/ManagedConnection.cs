using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;
using DanilovSoft.WebSockets;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.Buffers;
using Ms = System.Net.WebSockets;
using DanilovSoft.vRPC.Resources;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using DanilovSoft.vRPC.DTO;
using DanilovSoft.vRPC.Source;
using System.IO;
using DanilovSoft.vRPC.Context;
using DanilovSoft.vRPC.JsonRpc;
using System.Text.Json;
using System.Globalization;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Контекст соединения Web-Сокета. Владеет соединением.
    /// </summary>
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public abstract class ManagedConnection : IDisposable, IGetProxy
    {
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly InvokeActionsDictionary _invokeMethods;
        /// <summary>
        /// Для Completion.
        /// </summary>
        private readonly TaskCompletionSource<CloseReason> _completionTcs = new TaskCompletionSource<CloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>
        /// Взводится при обрыве соединения.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2213", Justification = "Не требует вызывать Dispose если гарантированно будет вызван Cancel")]
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        /// <summary>
        /// Срабатывает когда соединение переходит в закрытое состояние.
        /// </summary>
        public CancellationToken CompletionToken => _cts.Token;
        /// <summary>
        /// Причина закрытия соединения. Это свойство возвращает <see cref="Completion"/>.
        /// Запись через блокировку <see cref="DisconnectEventObj"/>.
        /// </summary>
        public CloseReason? DisconnectReason { get; private set; }
        /// <summary>
        /// Возвращает <see cref="Task"/> который завершается когда 
        /// соединение переходит в закрытое состояние.
        /// Возвращает <see cref="DisconnectReason"/>.
        /// Не мутабельное свойство.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion => _completionTcs.Task;
        internal bool IsServer { get; }
        public ServiceProvider ServiceProvider { get; }
        /// <summary>
        /// Подключенный TCP сокет.
        /// </summary>
        private readonly ManagedWebSocket _ws;
        /// <summary>
        /// Коллекция запросов ожидающие ответ от удалённой стороны.
        /// </summary>
        private readonly PendingRequestDictionary _responseAwaiters;
        public EndPoint LocalEndPoint { get; }
        public EndPoint RemoteEndPoint { get; }
        /// <summary>
        /// Отправка сообщений должна выполняться только через этот канал.
        /// </summary>
        private readonly Channel<IMessageToSend> _sendChannel;
        private int _disposed;
        /// <summary>
        /// <see langword="volatile"/>
        /// </summary>
        private bool IsDisposed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Свойство записывается через CAS поэтому что-бы прочитать нужен volatile.
                return Volatile.Read(ref _disposed) == 1;
            }
        }
        /// <summary>
        /// Не Null если происходит остановка сервиса.
        /// Используется для проверки возможности начать новый запрос.
        /// Использовать через блокировку <see cref="StopRequiredLock"/>.
        /// </summary>
        /// <remarks><see langword="volatile"/></remarks>
        private volatile ShutdownRequest? _shutdownRequest;
        /// <summary>
        /// Предотвращает повторный вызов Stop.
        /// </summary>
        private object StopRequiredLock => _completionTcs;
        /// <summary>
        /// Количество запросов для обработки и количество ответов для отправки.
        /// Для отслеживания грациозной остановки сервиса.
        /// </summary>
        private int _activeRequestCount;
        /// <summary>
        /// Подписку на событие Disconnected нужно синхронизировать что-бы подписчики не пропустили момент обрыва.
        /// </summary>
        private object DisconnectEventObj => _sendChannel;
        private EventHandler<SocketDisconnectedEventArgs>? _disconnected;
        /// <summary>
        /// Событие обрыва соединения. Может сработать только один раз.
        /// Если подписка на событие происходит к уже отключенному сокету то событие сработает сразу же.
        /// Гарантирует что событие не будет пропущено в какой бы момент не происходила подписка.
        /// </summary>
        public event EventHandler<SocketDisconnectedEventArgs> Disconnected
        {
            add
            {
                CloseReason? closeReason = null;
                lock (DisconnectEventObj)
                {
                    if (DisconnectReason == null)
                    {
                        _disconnected += value;
                    }
                    else
                    // Подписка к уже отключенному сокету.
                    {
                        closeReason = DisconnectReason;
                    }
                }

                if(closeReason != null && value != null)
                {
                    value(this, new SocketDisconnectedEventArgs(this, closeReason));
                }
            }
            remove
            {
                // Отписываться можно без блокировки — делегаты потокобезопасны.
                _disconnected -= value;
            }
        }
        private volatile bool _isConnected = true;
        /// <summary>
        /// Если значение – <see langword="false"/>, то можно узнать причину через свойство <see cref="DisconnectReason"/>.
        /// Когда значение становится <see langword="false"/>, то вызывается событие <see cref="Disconnected"/>.
        /// После разъединения текущий экземпляр не может быть переподключен.
        /// </summary>
        /// <remarks><see langword="volatile"/>.</remarks>
        public bool IsConnected => _isConnected;
        public abstract bool IsAuthenticated { get; }
        private Task? _senderTask;
        private bool _tcpNoDelay;

        // static ctor.
        static ManagedConnection()
        {
            //var proto = ProtoBuf.Serializer.GetProto<HeaderDto>();
            //Console.WriteLine(proto);

            //ManagedWebSocket.DefaultNoDelay = true;
            //Debug.Assert(System.Runtime.InteropServices.Marshal.SizeOf<HeaderDto>() <= 32, $"Структуру {nameof(HeaderDto)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<RequestMessageDto>() <= 16, $"Структуру {nameof(RequestMessageDto)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<RequestMessage>() <= 16, $"Структуру {nameof(RequestMessage)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<ValueWebSocketReceiveExResult>() <= 16, $"Структуру {nameof(ValueWebSocketReceiveExResult)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<CloseReason>() <= 16, $"Структуру {nameof(CloseReason)} лучше заменить на класс");

            // Прогрев сериализатора.
            //ProtoBuf.Serializer.PrepareSerializer<HeaderDto>();
            //ProtoBuf.Serializer.PrepareSerializer<MultipartHeaderDto>();
            //ExtensionMethods.WarmupRequestMessageJson();
        }

        // ctor.
        /// <summary>
        /// Принимает открытое соединение Web-Socket.
        /// </summary>
        internal ManagedConnection(ManagedWebSocket webSocket, bool isServer, ServiceProvider serviceProvider, InvokeActionsDictionary actions)
        {
            IsServer = isServer;

            Debug.Assert(webSocket.State == Ms.WebSocketState.Open);

            LocalEndPoint = webSocket.LocalEndPoint;
            RemoteEndPoint = webSocket.RemoteEndPoint;
            _ws = webSocket;
            _tcpNoDelay = webSocket.Socket.NoDelay;
            //_pipe = new Pipe();

            _responseAwaiters = new PendingRequestDictionary();

            // IoC готов к работе.
            ServiceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _invokeMethods = actions;

            _sendChannel = Channel.CreateUnbounded<IMessageToSend>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true, // Внимательнее с этим параметром!
                SingleReader = true,
                SingleWriter = false,
            });

            // Не может сработать сразу потому что пока не запущен 
            // поток чтения или отправки – некому спровоцировать событие.
            _ws.Disconnecting += WebSocket_Disconnected;
        }

        /// <summary>
        /// Запускает бесконечный цикл обработки запросов.
        /// </summary>
        internal void StartReceiveLoopThreads()
        {
            // Не бросает исключения.
            _senderTask = LoopSendAsync();

#if NETSTANDARD2_0 || NET472
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, state: this);
#else
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, state: this, preferLocal: false); // Через глобальную очередь.
#endif
        }

#if NETSTANDARD2_0 || NET472

        private static void ReceiveLoopStart(object? state)
        {
            var self = state as ManagedConnection;
            Debug.Assert(self != null);
            ReceiveLoopStart(argState: self!);
        }
#endif

        private static void ReceiveLoopStart(ManagedConnection argState)
        {
            // Не бросает исключения.
            argState.ReceiveLoop();
        }

        private void WebSocket_Disconnected(object? sender, SocketDisconnectingEventArgs e)
        {
            CloseReason closeReason;
            if (e.DisconnectingReason.Gracifully)
            {
                closeReason = CloseReason.FromCloseFrame(e.DisconnectingReason.CloseStatus, 
                    e.DisconnectingReason.CloseDescription, e.DisconnectingReason.AdditionalDescription, _shutdownRequest);
            }
            else
            {
                Debug.Assert(e.DisconnectingReason.Error != null);
                closeReason = CloseReason.FromException(e.DisconnectingReason.Error, _shutdownRequest, e.DisconnectingReason.AdditionalDescription);
            }
            TryDispose(closeReason);
        }
         
        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public CloseReason Shutdown(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            return InnerShutdownAsync(new ShutdownRequest(disconnectTimeout, closeDescription)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public Task<CloseReason> ShutdownAsync(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            return InnerShutdownAsync(new ShutdownRequest(disconnectTimeout, closeDescription));
        }

        /// <summary>
        /// Запрещает отправку новых запросов; Ожидает когда завершатся текущие запросы 
        /// и отправляет удалённой стороне сообщение о закрытии соединения с ожиданием подтверджения.
        /// Затем выполняет Dispose и взводит <see cref="Completion"/>.
        /// </summary>
        /// <remarks>Не бросает исключения. Потокобезопасно.</remarks>
        internal async Task<CloseReason> InnerShutdownAsync(ShutdownRequest stopRequired)
        {
            bool firstTime;
            lock (StopRequiredLock)
            {
                if (_shutdownRequest == null)
                {
                    firstTime = true;

                    // Запретить выполнять новые запросы.
                    // Запомнить причину отключения что-бы позднее передать её удалённой стороне.
                    _shutdownRequest = stopRequired; // volatile.

                    if (!DecreaseActiveRequestsCount())
                    // Нет ни одного ожадающего запроса.
                    {
                        // Можно безопасно остановить сокет.
                        // Не бросает исключения.
                        TryBeginClose(stopRequired.CloseDescription);
                    }
                    // Иначе другие потоки уменьшив переменную увидят что флаг стал -1
                    // Это будет соглашением о необходимости остановки.
                }
                else
                {
                    firstTime = false;
                    stopRequired = _shutdownRequest;
                }
            }

            if (firstTime)
            {
                if (stopRequired.ShutdownTimeout > TimeSpan.Zero)
                {
                    var timeoutTask = Task.Delay(stopRequired.ShutdownTimeout);

                    // Подождать грациозную остановку.
                    if (await Task.WhenAny(_completionTcs.Task, timeoutTask).ConfigureAwait(false) == timeoutTask)
                    {
                        Debug.WriteLine($"Достигнут таймаут {stopRequired.ShutdownTimeout.TotalSeconds:0.#} сек. на грациозную остановку.");
                    }
                }

                // Не бросает исключения.
                TryDispose(CloseReason.FromException(new VRpcShutdownException(stopRequired)));

                CloseReason closeReason = _completionTcs.Task.Result;

                // Передать результат другим потокам которые вызовут Shutdown.
                stopRequired.SetTaskResult(closeReason);

                return closeReason;
            }
            else
            {
                return await stopRequired.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Устанавливает причину закрытия соединения для текущего экземпляра и закрывает соединение.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void TryDisposeOnCloseReceived()
        {
            // Был получен Close. Это значит что веб сокет уже закрыт и нам остаётся только закрыть сервис.
            TryDispose(CloseReason.FromCloseFrame(_ws.CloseStatus, _ws.CloseStatusDescription, null, _shutdownRequest));
        }

        /// <summary>
        /// Отправляет сообщение Close и ожидает ответный Close. Затем закрывает соединение.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private async void TryBeginClose(string? closeDescription)
        {
            // Эту функцию вызывает тот поток который поймал флаг о необходимости завершения сервиса.
            // Благодаря событию WebSocket.Disconnect у нас гарантированно вызовется AtomicDispose.

            // Нельзя делать Close одновременно с Send операцией.
            if (await TryCompleteSenderAsync().ConfigureAwait(false))
            // Send больше никто не сделает.
            {
                try
                {
                    // Отправить Close с ожиданием ответного Close.
                    // Может бросить исключение если сокет уже в статусе Close.
                    await _ws.CloseAsync(Ms.WebSocketCloseStatus.NormalClosure, closeDescription, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Оповестить об обрыве.
                    TryDispose(CloseReason.FromException(ex, _shutdownRequest));

                    // Завершить поток.
                    return;
                }
            }
        }

        /// <summary>
        /// Отправляет сообщение Close и ожидает ответный Close. Затем закрывает соединение.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void TryBeginSendCloseBeforeShutdown()
        {
            Debug.Assert(_shutdownRequest != null);

            TryBeginClose(_shutdownRequest.CloseDescription);
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны сервера.</remarks>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal ValueTask OnServerNotificationCall(RequestMethodMeta methodMeta, object[] args)
        {
            Debug.Assert(methodMeta.IsNotificationRequest);

            // Сериализуем запрос в память.
            SerializedMessageToSend serMsg = methodMeta.SerializeRequest(args);
            SerializedMessageToSend? serMsgToDispose = serMsg;
            try
            {
                // Отправляем запрос.
                ValueTask task = SendNotificationAsync(serMsg);
                serMsgToDispose = null;
                return task;
            }
            finally
            {
                serMsgToDispose?.Dispose();
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны сервера.</remarks>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal Task<TResult> OnServerMethodCall<TResult>(RequestMethodMeta methodMeta, object[] args)
        {
            Debug.Assert(!methodMeta.IsNotificationRequest);

            if (methodMeta.IsJsonRpc)
            {
                return ExecuteJsonRequest<TResult>(methodMeta, args);
            }
            else
            {
                // Сериализуем запрос в память.
                SerializedMessageToSend serMsg = methodMeta.SerializeRequest(args);
                SerializedMessageToSend? serMsgToDispose = serMsg;
                try
                {
                    // Отправляем запрос.
                    Task<TResult> task = SendSerializedRequestAndWaitResponse<TResult>(methodMeta, serMsg);
                    serMsgToDispose = null;
                    return task;
                }
                finally
                {
                    serMsgToDispose?.Dispose();
                }
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны клиента.</remarks>
        /// <exception cref="Exception">Могут быть исключения не инкапсулированные в Task.</exception>
        //[SuppressMessage("Reliability", "CA2000:Ликвидировать объекты перед потерей области", Justification = "Анализатор не понимает смену ответственности на Channel")]
        internal static Task<TResult> OnClientMethodCall<TResult>(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta methodMeta, object[] args)
        {
            Debug.Assert(!methodMeta.IsNotificationRequest);

            if (methodMeta.IsJsonRpc)
            {
                return ExecuteJsonRequest<TResult>(connectionTask, methodMeta, args);
            }
            else
            {
                // Сериализуем запрос в память. Лучше выполнить до завершения подключения.
                SerializedMessageToSend serMsg = methodMeta.SerializeRequest(args);
                SerializedMessageToSend? serMsgToDispose = serMsg;
                try
                {
                    // Может начать отправку текущим потоком. Диспозит serMsg в случае ошибки.
                    Task<TResult> pendingRequestTask = SendClientRequestAndGetResultStatic<TResult>(connectionTask, serMsg, methodMeta);
                    serMsgToDispose = null; // Предотвратить Dispose.
                    return pendingRequestTask;
                }
                finally
                {
                    // В случае исключения в методе ExecuteRequestStatic
                    // объект может быть уже уничтожен но это не страшно, его Dispose - атомарный.
                    serMsgToDispose?.Dispose();
                }
            }
        }

        /// <exception cref="Exception">Могут быть исключения не инкапсулированные в Task.</exception>
        private static Task<TResult> ExecuteJsonRequest<TResult>(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta methodMeta, object[] args)
        {
            Debug.Assert(methodMeta.IsJsonRpc);
            Debug.Assert(!methodMeta.IsNotificationRequest);

            if (connectionTask.IsCompleted)
            {
                // Может быть исключение если не удалось подключиться.
                ClientSideConnection connection = connectionTask.GetAwaiter().GetResult();

                return connection.ExecuteJsonRequest<TResult>(methodMeta, args);
            }
            else
            {
                return WaitAsync(connectionTask, methodMeta, args).Unwrap();

                static async Task<Task<TResult>> WaitAsync(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta methodMeta, object[] args)
                {
                    var connection = await connectionTask.ConfigureAwait(false);

                    return connection.ExecuteJsonRequest<TResult>(methodMeta, args);
                }
            }
        }

        private Task<TResult> ExecuteJsonRequest<TResult>(RequestMethodMeta methodMeta, object[] args)
        {
            // Shutdown нужно проверять раньше чем Dispose потому что Dispose может быть по причине Shutdown.
            if (_shutdownRequest == null) // volatile проверка.
            {
                if (!IsDisposed) // volatile проверка.
                {
                    // Добавить запрос в словарь для дальнейшей связки с ответом.
                    ResponseAwaiter<TResult> responseAwaiter = _responseAwaiters.AddRequest<TResult>(methodMeta, out int uid);

                    var request = new JsonRpcRequest(responseAwaiter, methodMeta, args, uid);

                    TryPostMessage(request);

                    // Ожидаем результат от потока поторый читает из сокета.
                    return responseAwaiter.Task;
                }
                else
                    return Task.FromException<TResult>(new ObjectDisposedException(GetType().FullName));
            }
            else
                return Task.FromException<TResult>(new VRpcShutdownException(_shutdownRequest));
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны клиента.</remarks>
        /// <exception cref="Exception">Могут быть исключения не инкапсулированные в Task.</exception>
        //[SuppressMessage("Reliability", "CA2000:Ликвидировать объекты перед потерей области", Justification = "Анализатор не понимает смену ответственности на Channel")]
        internal static ValueTask OnClientNotificationCall(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta methodMeta, object[] args)
        {
            Debug.Assert(methodMeta.IsNotificationRequest);

            // Сериализуем запрос в память. Лучше выполнить до завершения подключения.
            SerializedMessageToSend serMsg = methodMeta.SerializeRequest(args);
            SerializedMessageToSend? serMsgToDispose = serMsg;
            try
            {
                // Может начать отправку текущим потоком. Диспозит serMsg в случае ошибки.
                ValueTask task = WaitConnectionAndSendNotificationAsync(connectionTask, serMsg);
                serMsgToDispose = null; // Предотвратить Dispose.
                return task;
            }
            finally
            {
                // В случае исключения в методе ExecuteRequestStatic
                // объект может быть уже уничтожен но это не страшно, его Dispose - атомарный.
                serMsgToDispose?.Dispose();
            }
        }

        /// <summary>
        /// Ожидает завершение подключения к серверу и передаёт сообщение в очередь на отправку.
        /// Может бросить исключение.
        /// Чаще всего возвращает <see cref="Task.CompletedTask"/>.
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private static ValueTask WaitConnectionAndSendNotificationAsync(ValueTask<ClientSideConnection> connectingTask, SerializedMessageToSend serMsg)
        {
            Debug.Assert(serMsg.MessageToSend.IsNotificationRequest);

            if (connectingTask.IsCompleted)
            {
                // Может бросить исключение.
                ManagedConnection connection = connectingTask.Result;
                
                // Отправляет уведомление через очередь.
                ValueTask pendingSendTask = connection.SendNotificationAsync(serMsg);

                return pendingSendTask;
            }
            else
            // Подключение к серверу ещё не завершено.
            {
                return WaitForConnectAndSendNotification(connectingTask, serMsg);
            }

            // Локальная функция.
            static async ValueTask WaitForConnectAndSendNotification(ValueTask<ClientSideConnection> conTask, SerializedMessageToSend serializedMessage)
            {
                ClientSideConnection connection = await conTask.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                await connection.SendNotificationAsync(serializedMessage).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Отправляет запрос и возвращает результат.
        /// Может бросить исключение.
        /// </summary>
        /// <remarks>Со стороны клиента.</remarks>
        private static Task<TResult> SendClientRequestAndGetResultStatic<TResult>(ValueTask<ClientSideConnection> connectionTask, SerializedMessageToSend serMsg, RequestMethodMeta methodMeta)
        {
            Debug.Assert(!methodMeta.IsNotificationRequest);

            if (connectionTask.IsCompleted)
            {
                // Может быть исключение если не удалось подключиться.
                ClientSideConnection connection = connectionTask.GetAwaiter().GetResult();

                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendSerializedRequestAndWaitResponse<TResult>(methodMeta, serMsg);
            }
            else
            {
                return WaitForConnectAndSendRequest(connectionTask, serMsg, methodMeta).Unwrap();
            }

            static async Task<Task<TResult>> WaitForConnectAndSendRequest(ValueTask<ClientSideConnection> t, SerializedMessageToSend serMsg, RequestMethodMeta requestMeta)
            {
                ClientSideConnection connection = await t.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendSerializedRequestAndWaitResponse<TResult>(requestMeta, serMsg);
            }
        }

        /// <summary>
        /// Отправляет запрос-уведомление через очередь (выполнит отправку текущим потоком если очередь пуста).
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal ValueTask SendNotificationAsync(SerializedMessageToSend serializedMessage)
        {
            Debug.Assert(serializedMessage.MessageToSend.IsNotificationRequest);

            if (_shutdownRequest == null)
            {
                if (!IsDisposed)
                {
                    //ValidateAuthenticationRequired(requestMeta);

                    TryPostMessage(serializedMessage);

                    return serializedMessage.WaitNotificationAsync();
                }
                else
                    return new ValueTask(Task.FromException(new ObjectDisposedException(GetType().FullName)));
            }
            else
                return new ValueTask(Task.FromException(new VRpcShutdownException(_shutdownRequest)));
        }

        /// <summary>
        /// Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private protected Task<TResult> SendRequestAndWaitResponse<TResult>(RequestMethodMeta requestMeta, object[] args)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            SerializedMessageToSend serMsgRequest = requestMeta.SerializeRequest(args);
            SerializedMessageToSend? serMsgToDispose = serMsgRequest;
            try
            {
                Task<TResult> pendingRequestTask = SendSerializedRequestAndWaitResponse<TResult>(requestMeta, serMsgRequest);
                serMsgToDispose = null;
                return pendingRequestTask;
            }
            finally
            {
                serMsgToDispose?.Dispose();
            }
        }

        /// <summary>
        /// Отправляет запрос и ожидает ответ.
        /// Передаёт владение объектом <paramref name="serMsg"/> другому потоку.
        /// </summary>
        /// <remarks>Происходит при обращении к прокси-интерфейсу.</remarks>
        /// <exception cref="SocketException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <returns>Таск с результатом от сервера.</returns>
        private protected Task<TResult> SendSerializedRequestAndWaitResponse<TResult>(RequestMethodMeta requestMeta, SerializedMessageToSend serMsg)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            // Shutdown нужно проверять раньше чем Dispose потому что Dispose может быть по причине Shutdown.
            if (_shutdownRequest == null) // volatile проверка.
            {
                if (!IsDisposed) // volatile проверка.
                {
                    SerializedMessageToSend? serMsgToDispose = serMsg;
                    try
                    {
                        // Добавить запрос в словарь для дальнейшей связки с ответом.
                        ResponseAwaiter<TResult> responseAwaiter = _responseAwaiters.AddRequest<TResult>(requestMeta, out int uid);

                        // Назначить запросу уникальный идентификатор.
                        serMsg.Uid = uid;

                        // Планируем отправку запроса.
                        // Не бросает исключения.
                        TryPostMessage(serMsg);

                        // Мы больше не владеем этим объектом.
                        serMsgToDispose = null;

                        // Ожидаем результат от потока поторый читает из сокета.
                        return responseAwaiter.Task;
                    }
                    finally
                    {
                        serMsgToDispose?.Dispose();
                    }
                }
                else
                    return Task.FromException<TResult>(new ObjectDisposedException(GetType().FullName));
            }
            else
                return Task.FromException<TResult>(new VRpcShutdownException(_shutdownRequest));
        }

        /// <summary>
        /// Здесь не должно быть глубокого async стека для сохранения высокой производительности.
        /// </summary>
        private async void ReceiveLoop()
        {
            byte[] headerBuffer = new byte[HeaderDto.HeaderMaxSize];

            // Бесконечно обрабатываем сообщения сокета.
            while (!IsDisposed)
            {
                #region Читаем хедер

                ValueWebSocketReceiveResult webSocketMessage;
                int bufferOffset = 0;
                do
                {
                    Memory<byte> slice = headerBuffer.AsMemory(bufferOffset);
                    if (!slice.IsEmpty)
                    {
                        try
                        {
                            // Читаем фрейм веб-сокета.
                            webSocketMessage = await _ws.ReceiveExAsync(slice, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве и завершить поток.
                            TryDispose(CloseReason.FromException(ex, _shutdownRequest));
                            return;
                        }
                        bufferOffset += webSocketMessage.Count;
                    }
                    else
                    {
                        Debug.Assert(false, "Превышен размер хедера");

                        // Отправка Close и завершить поток.
                        await SendCloseHeaderSizeErrorAsync().ConfigureAwait(false);
                        return;
                    }
                } while (!webSocketMessage.EndOfMessage);

                #endregion

                #region Десериализуем хедер

                HeaderDto header;
                if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Binary)
                {
                    try
                    {
                        header = CustomSerializer.DeserializeHeader(headerBuffer.AsSpan(0, webSocketMessage.Count));
                        header.ValidateDeserializedHeader();
                    }
                    catch (Exception headerException)
                    // Не удалось десериализовать заголовок.
                    {
                        // Отправка Close и завершить поток.
                        await SendCloseHeaderErrorAsync(webSocketMessage.Count, headerException).ConfigureAwait(false);
                        return;
                    }
                }
                else if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Text)
                // Получен Json-rpc запрос или ответ.
                {
                    await HandleJrpcMessage(headerBuffer.AsSpan(0, webSocketMessage.Count), out bool success).ConfigureAwait(false);

                    if (success)
                        continue;
                    else
                        return;
                }
                else
                // Получен Close.
                {
                    await TryNonBinaryTypeCloseAndDisposeAsync(webSocketMessage).ConfigureAwait(false);
                    return;
                }
                #endregion

                if (header != default)
                {
                    #region Читаем контент

                    using (IMemoryOwner<byte> contentMemHandler = MemoryPool<byte>.Shared.Rent(header.PayloadLength))
                    {
                        Memory<byte> contentMem = null;

                        if (header.PayloadLength > 0)
                        // Есть дополнительный фрейм с телом сообщения.
                        {
                            #region Нужно прочитать весь контент

                            // Можно не очищать – буффер будет перезаписан.
                            contentMem = contentMemHandler.Memory.Slice(0, header.PayloadLength);

                            bufferOffset = 0;

                            // Сколько байт должны принять в следующих фреймах.
                            int receiveMessageBytesLeft = header.PayloadLength;

                            do // Читаем и склеиваем фреймы веб-сокета пока не EndOfMessage.
                            {
                                // Проверить на ошибку протокола.
                                if (receiveMessageBytesLeft == 0)
                                // Считали сколько заявлено в ContentLength но сообщение оказалось больше.
                                {
                                    // Отправка Close и завершить поток.
                                    await SendCloseContentSizeErrorAsync().ConfigureAwait(false);
                                    return;
                                }

                                #region Пока не EndOfMessage записывать в буфер памяти

                                #region Читаем фрейм веб-сокета

                                // Ограничиваем буфер памяти до колличества принятых байт из сокета.
                                Memory<byte> contentBuffer = contentMem.Slice(bufferOffset, receiveMessageBytesLeft);
                                try
                                {
                                    // Читаем фрейм веб-сокета.
                                    webSocketMessage = await _ws.ReceiveExAsync(contentBuffer, CancellationToken.None).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве и завершить поток.
                                    TryDispose(CloseReason.FromException(ex, _shutdownRequest));
                                    return;
                                }
                                #endregion

                                if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Binary)
                                {
                                    bufferOffset += webSocketMessage.Count;
                                    receiveMessageBytesLeft -= webSocketMessage.Count;
                                }
                                else
                                // Получен Close или Text.
                                {
                                    // Оповестить и завершить поток.
                                    await TryNonBinaryTypeCloseAndDisposeAsync(webSocketMessage).ConfigureAwait(false);
                                    return;
                                }
                                #endregion

                            } while (!webSocketMessage.EndOfMessage);
                            #endregion
                        }

                        // У сообщения может не быть контента.
                        if (!TryProcessPayload(in header, contentMem))
                        {
                            // Завершить поток.
                            return;
                        }
                    }
                    #endregion
                }
                else
                // Ошибка в хедере.
                {
                    // Отправка Close и выход.
                    await SendCloseHeaderErrorAsync(webSocketMessage.Count).ConfigureAwait(false);
                    return;
                }
            }
        }

        private Task HandleJrpcMessage(ReadOnlySpan<byte> buffer, out bool success)
        {
            try
            {
                DeserializeJsonRpcMessage(buffer);
            }
            catch (JsonException ex)
            {
                // Отправка Close и завершить поток.
                success = false;
                return SendCloseParseErrorAsync(ex);
            }
            success = true;
            return Task.CompletedTask;

            //if (deserialized)
            //{
            //    if (TryIncreaseActiveRequestsCount(isResponseRequired: request.Id != null))
            //    {
            //        // Начать выполнение запроса в отдельном потоке.
            //        StartProcessRequest(new RequestContext(request.Id.Value, request.Method, request.Args));
            //    }
            //    else
            //    {
            //        // Завершить поток.
            //        success = false;
            //        return Task.CompletedTask;
            //    }

            //    success = true;
            //    return Task.CompletedTask;
            //}
            //else
            //{
            //    if (request.Id != null)
            //    {
            //        // Передать на отправку результат с ошибкой через очередь.
            //        PostSendResponse(new ResponseMessage(request.Id.Value, error));
            //        success = true;
            //        return Task.CompletedTask;
            //    }
            //    else
            //    {
            //        success = false;
            //        return TryProtocolErrorCloseAsync("Invalid Request");
            //    }
            //}
        }

        /// <exception cref="JsonException"/>
        internal void DeserializeJsonRpcMessage(ReadOnlySpan<byte> utf8Json)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            string? actionName = null;
            int? id = null;
            ControllerMethodMeta? method = null;
            object[]? args = null;

            //result = new JsonRequest();

            //bool gotMethod = false;
            bool gotId = false;
            JsonReaderState paramsState;

            var reader = new Utf8JsonReader(utf8Json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (method == null && reader.ValueTextEquals("method"))
                    {
                        if (reader.Read())
                        {
                            actionName = reader.GetString();
                            if (_invokeMethods.TryGetAction(actionName, out method))
                            {
                                args = method.Parametergs.Length == 0
                                    ? Array.Empty<object>()
                                    : (new object[method.Parametergs.Length]);
                            }
                        }
                    }
                    else if (!gotId && reader.ValueTextEquals("id"))
                    {
                        if (reader.Read())
                        {
                            // TODO формат может быть String или Number.
                            if (reader.TokenType == JsonTokenType.Number)
                            {
                                id = reader.GetInt32();
                                gotId = true;
                            }
                            else if (reader.TokenType == JsonTokenType.String)
                            {
                                id = int.Parse(reader.GetString(), CultureInfo.InvariantCulture);
                            }
                        }
                    }
                    else if (reader.ValueTextEquals("result"))
                    {
                        if (gotId)
                        {
                            if (reader.Read())
                            {
                                if (_responseAwaiters.TryRemove(id.Value, out IResponseAwaiter? awaiter))
                                {
                                    awaiter.DeserializeJsonRpcResponse(ref reader);
                                    return;
                                }
                                else
                                {

                                }
                            }
                        }
                    }
                    else if (reader.ValueTextEquals("params"))
                    {
                        if (method != null)
                        {
                            if (!TryReadArgs(method, args!, reader, out var error))
                            {
                                return;
                            }
                        }
                        else
                        {
                            paramsState = reader.CurrentState;
                            // TODO
                        }
                    }
                    else if (reader.ValueTextEquals("code"))
                    {
                        if (reader.Read())
                        {
                            reader.GetInt32();
                        }
                    }
                }
            }

            if (method == null && id != null && actionName != null)
            {
                // Передать на отправку результат с ошибкой через очередь.
                PostSendResponse(new ResponseMessage(id.Value, ResponseHelper.MethodNotFound(actionName)));
                return;
            }

            return;
        }

        private static bool TryReadArgs(ControllerMethodMeta method, object[] args, Utf8JsonReader reader, [MaybeNullWhen(true)] out IActionResult? error)
        {
            if (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    // Считаем сколько аргументов есть в json'е.
                    short argsInJsonCounter = 0;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (method.Parametergs.Length > argsInJsonCounter)
                        {
                            Type paramType = method.Parametergs[argsInJsonCounter].ParameterType;
                            try
                            {
                                args[argsInJsonCounter] = JsonSerializer.Deserialize(ref reader, paramType);
                            }
                            catch (JsonException)
                            {
                                error = ResponseHelper.ErrorDeserializingArgument(method.MethodFullName, argIndex: argsInJsonCounter, paramType);
                                return false;
                            }
                            argsInJsonCounter++;
                        }
                        else
                        // Несоответствие числа параметров.
                        {
                            error = ResponseHelper.ArgumentsCountMismatchError(method.MethodFullName, method.Parametergs.Length);
                            return false;
                        }
                    }
                }
            }
            error = default;
            return true;
        }

        /// <summary>При получении Close или сообщения типа Text — отправляет Close и делает Dispose.</summary>
        /// <remarks>Не бросает исключения.</remarks>
        private Task TryNonBinaryTypeCloseAndDisposeAsync(ValueWebSocketReceiveResult webSocketMessage)
        {
            Debug.Assert(webSocketMessage.MessageType != Ms.WebSocketMessageType.Binary);

            if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Text)
            // Наш протокол не поддерживает сообщения типа Text.
            {
                // Отправка Close и завершить поток.
                return TrySendNotSupportedTypeAndCloseAsync();
            }
            else
            // Получен Close.
            {
                // Оповестить и завершить поток.
                return TryCloseReceivedAsync();
            }
        }

        /// <summary>
        /// Десериализует Payload и выполняет запрос или передаёт ответ ожидающему потоку.
        /// </summary>
        /// <param name="payload">Контент запроса целиком.</param>
        /// <returns>false если нужно завершить поток чтения.</returns>
        private bool TryProcessPayload(in HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            if (header.IsRequest)
            // Получен запрос.
            {
                if (TryIncreaseActiveRequestsCount(header.IsResponseRequired))
                {
                    if (ValidateHeader(in header))
                    {
                        if (TryGetRequestMethod(in header, out ControllerMethodMeta? method))
                        {
                            try
                            {
                                #region Десериализация запроса

                                if (CustomSerializer.TryDeserializeRequest(payload, method, in header, out RequestContext requestToInvoke, out IActionResult? error))
                                {
                                    #region Выполнение запроса

                                    //Debug.Assert(requestToInvoke != null, "Не может быть Null когда success");

                                    // Начать выполнение запроса в отдельном потоке.
                                    StartProcessRequest(in requestToInvoke);

                                    //requestToInvoke = null; // Предотвратить Dispose.

                                    #endregion
                                }
                                else
                                // Не удалось десериализовать запрос.
                                {
                                    #region Игнорируем запрос

                                    if (header.IsResponseRequired)
                                    // Запрос ожидает ответ.
                                    {
                                        Debug.Assert(error != null, "Не может быть Null когда !success");
                                        Debug.Assert(header.Id != null, "Не может быть Null когда IsResponseRequired");

                                        // Передать на отправку результат с ошибкой через очередь.
                                        PostSendResponse(new ResponseMessage(header.Id.Value, error));
                                    }

                                    // Вернуться к чтению следующего сообщения.
                                    return true;

                                    #endregion
                                }
                                #endregion
                            }
                            finally
                            {
                                //requestToInvoke?.Dispose();
                            }
                        }
                        else
                        {
                            // Вернуться к чтению следующего сообщения.
                            return true;
                        }
                    }
                    else
                    // Хедер не валиден.
                    {
                        // Завершать поток чтения не нужно (вернуться к чтению следующего сообщения).
                        return true;
                    }
                }
                else
                // Происходит остановка. Выполнять запрос не нужно.
                {
                    return false; // Завершить поток чтения.
                }
            }
            else
            // Получен ответ на запрос.
            {
                #region Передача другому потоку ответа на запрос

                Debug.Assert(header.Id != null, "У ответа на запрос должен быть идентификатор");

                // Удалить запрос из словаря.
                if (header.Id != null && _responseAwaiters.TryRemove(header.Id.Value, out IResponseAwaiter? respAwaiter))
                // Передать ответ ожидающему потоку.
                {
                    respAwaiter.DeserializeAndSetResponse(in header, payload);

                    // Получен ожидаемый ответ на запрос.
                    if (DecreaseActiveRequestsCount())
                    {
                        return true;
                    }
                    else
                    // Пользователь запросил остановку сервиса.
                    {
                        // Не бросает исключения.
                        TryBeginSendCloseBeforeShutdown();

                        // Завершить поток.
                        return false;
                    }
                }
                #endregion
            }
            return true; // Завершать поток чтения не нужно (вернуться к чтению следующего сообщения).
        }

        private bool TryGetRequestMethod(in HeaderDto header, [NotNullWhen(true)] out ControllerMethodMeta? method)
        {
            Debug.Assert(header.MethodName != null);

            if (_invokeMethods.TryGetAction(header.MethodName, out method))
            {
                return true;
            }
            else
            // Не найден метод контроллера.
            {
                if (header.IsResponseRequired)
                // Запрос ожидает ответ.
                {
                    Debug.Assert(header.Id != null);

                    var error = ResponseHelper.MethodNotFound(header.MethodName);

                    // Передать на отправку результат с ошибкой через очередь.
                    PostSendResponse(new ResponseMessage(header.Id.Value, error));
                }
                return false;
            }
        }

        /// <returns>true если хедер валиден.</returns>
        private bool ValidateHeader(in HeaderDto header)
        {
            if (!header.IsRequest || !string.IsNullOrEmpty(header.MethodName))
            {
                return true;
            }
            else
            // Запрос ожидает ответ.
            {
                Debug.Assert(header.Id != null, "Не может быть Null когда IsResponseRequired");

                // Передать на отправку результат с ошибкой через очередь.
                PostSendResponse(new ResponseMessage(header.Id.Value, new InvalidRequestResult("В заголовке запроса отсутствует имя метода")));

                return false;
            }
        }

        /// <summary>
        /// Увеличивает счётчик активных запросов.
        /// </summary>
        /// <returns>true если разрешено продолжить выполнение запроса, 
        /// false если требуется остановить сервис.</returns>
        private bool TryIncreaseActiveRequestsCount(bool isResponseRequired)
        {
            if (isResponseRequired)
            {
                if (IncreaseActiveRequestsCount())
                {
                    return true;
                }
                else
                // Происходит остановка. Выполнять запрос не нужно.
                {
                    return false;
                }
            }
            else
            {
                return true;
            }
        }

        /// <summary>Отправляет Close и делает Dispose.</summary>
        /// <remarks>Не бросает исключения.</remarks>
        private Task TryCloseReceivedAsync()
        {
            if (_ws.State == Ms.WebSocketState.CloseReceived)
            {
                return TryFinishSenderAndSendCloseAsync();
            }

            TryDisposeOnCloseReceived();
            return Task.CompletedTask;
        }

        /// <remarks>Не бросает исключения.</remarks>
        private Task TryFinishSenderAndSendCloseAsync()
        {
            ValueTask<bool> task = TryCompleteSenderAsync();
            if (task.IsCompletedSuccessfully)
            {
                return OnCompletedSender(task.Result);
            }
            else
            {
                return WaitAsync(task);
            }

            async Task WaitAsync(ValueTask<bool> task)
            {
                bool success = await task.ConfigureAwait(false);
                await OnCompletedSender(success).ConfigureAwait(false);
            }
        }

        private async Task OnCompletedSender(bool success)
        {
            if (success)
            {
                try
                {
                    await _ws.CloseOutputAsync(_ws.CloseStatus!.Value, _ws.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                // Обрыв соединения.
                {
                    // Оповестить об обрыве и завершить поток.
                    TryDispose(CloseReason.FromException(ex, _shutdownRequest));
                    return;
                }
            }
            TryDisposeOnCloseReceived();
        }

        private Task SendCloseContentSizeErrorAsync()
        {
            // Размер данных оказался больше чем заявлено в ContentLength.
            var protocolErrorException = new VRpcProtocolErrorException("Размер сообщения оказался больше чем заявлено в заголовке");

            // Отправка Close.
            return TryCloseAndDisposeAsync(protocolErrorException, "Web-Socket message size was larger than specified in the header's 'ContentLength'");
        }

        /// <summary>
        /// Отправляет Close и делает Dispose.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private Task TrySendNotSupportedTypeAndCloseAsync()
        {
            return TryProtocolErrorCloseAsync(SR.TextMessageTypeNotSupported);
        }

        /// <summary>
        /// Закрывает WebSocket с кодом 1002: ProtocolError и сообщением <paramref name="message"/>.
        /// Распространяет исключение <see cref="VRpcProtocolErrorException"/>.
        /// </summary>
        private Task TryProtocolErrorCloseAsync(string message)
        {
            // Тип фрейма должен быть Binary.
            var protocolErrorException = new VRpcProtocolErrorException(message);

            // Отправка Close.
            return TryCloseAndDisposeAsync(protocolErrorException, message);
        }

        /// <remarks>Json-RPC</remarks>
        private Task SendCloseParseErrorAsync(JsonException parseException)
        {
            var propagateException = new VRpcProtocolErrorException(SR2.GetString(SR.JsonRpcProtocolError, parseException.Message), innerException: parseException);

            // Отправка Close.
            return TryCloseAndDisposeAsync(propagateException, "Parse error (-32700)");
        }

        private Task SendCloseHeaderErrorAsync(int webSocketMessageCount, Exception headerException)
        {
            // Отправка Close.
            var propagateException = new VRpcProtocolErrorException(SR2.GetString(SR.ProtocolError, headerException.Message), headerException);
            return TryCloseAndDisposeAsync(propagateException, $"Unable to deserialize header. Count of bytes was {webSocketMessageCount}");
        }

        private Task SendCloseHeaderErrorAsync(int webSocketMessageCount)
        {
            // Отправка Close.
            var protocolException = new VRpcProtocolErrorException("Не удалось десериализовать полученный заголовок сообщения.");
            return TryCloseAndDisposeAsync(protocolException, $"Unable to deserialize header. Count of bytes was {webSocketMessageCount}");
        }

        private Task SendCloseHeaderSizeErrorAsync()
        {
            // Отправка Close.
            var protocolException = new VRpcProtocolErrorException($"Не удалось десериализовать полученный заголовок " +
                $"сообщения — превышен размер заголовка в {HeaderDto.HeaderMaxSize} байт.");

            return TryCloseAndDisposeAsync(protocolException, $"Unable to deserialize header. {HeaderDto.HeaderMaxSize} byte header size exceeded.");
        }

        /// <summary>
        /// Гарантирует что ничего больше не будет отправлено через веб-сокет. 
        /// Дожидается завершения отправляющего потока.
        /// Атомарно возвращает true означающее что поток должен выполнить Close.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        /// <returns>true если текущий поток запрыл канал, false если канал уже был закрыт другим потоком.</returns>
        private ValueTask<bool> TryCompleteSenderAsync()
        {
            bool completed = _sendChannel.Writer.TryComplete();
            Task? senderTask = Volatile.Read(ref _senderTask);
            if (senderTask != null)
            // Подождать завершение Send потока.
            {
                if (!senderTask.IsCompleted)
                {
                    return new ValueTask<bool>(task: WaitAsync(senderTask, completed));
                }
            }
            return new ValueTask<bool>(result: completed);

            static async Task<bool> WaitAsync(Task senderTask, bool completed)
            {
                await senderTask.ConfigureAwait(false);
                return completed;
            }
        }

        /// <summary>
        /// Отправляет Close если канал ещё не закрыт и выполняет Dispose.
        /// Распространяет исключение <paramref name="protocolErrorException"/> всем ожидаюшим потокам.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        /// <param name="protocolErrorException">Распространяет исключение ожидаюшим потокам.</param>
        private async Task TryCloseAndDisposeAsync(VRpcProtocolErrorException protocolErrorException, string closeDescription)
        {
            // Сообщить потокам что обрыв произошел по вине удалённой стороны.
            _responseAwaiters.TryPropagateExceptionAndLockup(protocolErrorException);

            if (await TryCompleteSenderAsync().ConfigureAwait(false))
            {
                try
                {
                    // Отключаемся от сокета с небольшим таймаутом.
                    using (var cts = new CancellationTokenSource(1_000))
                    {
                        await _ws.CloseAsync(Ms.WebSocketCloseStatus.ProtocolError, closeDescription, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                // Злой обрыв соединения.
                {
                    // Оповестить об обрыве.
                    TryDispose(CloseReason.FromException(ex, _shutdownRequest));

                    // Завершить поток.
                    return;
                }
            }

            // Оповестить об обрыве.
            TryDispose(CloseReason.FromException(protocolErrorException, _shutdownRequest));

            // Завершить поток.
            return;
        }

        /// <summary>
        /// Сериализует ответ в новом потоке и отправляет через очередь.
        /// Уменьшит <see cref="_activeRequestCount"/>.
        /// </summary>
        /// <remarks>Исключение по вине пользователеля крашнет процесс.</remarks>
        private void PostSendResponse(ResponseMessage responseToSend)
        {
#if NETSTANDARD2_0 || NET472
            ThreadPool.UnsafeQueueUserWorkItem(SerializeResponseAndTrySendThreadEntryPoint, Tuple.Create(this, responseToSend));
#else
            // Предпочитаем глобальную очередь.
            ThreadPool.UnsafeQueueUserWorkItem(SerializeResponseAndTrySendThreadEntryPoint, Tuple.Create(this, responseToSend), preferLocal: false);
#endif
        }

#if NETSTANDARD2_0 || NET472

        // Точка входа потока тред-пула.
        /// <remarks>Не бросает исключения.</remarks>
        private static void SerializeResponseAndTrySendThreadEntryPoint(object? state)
        {
            var tuple = state as Tuple<ManagedConnection, ResponseMessage>;
            Debug.Assert(tuple != null);

            SerializeResponseAndTrySendThreadEntryPoint(argState: tuple);
        }
#endif
        // Точка входа потока тред-пула.
        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private static void SerializeResponseAndTrySendThreadEntryPoint(Tuple<ManagedConnection, ResponseMessage> argState)
        {
            // Сериализуем.
            SerializedMessageToSend serializedMessage = SerializeResponse(argState.Item2);

            // Ставим в очередь.
            argState.Item1.TryPostMessage(serializedMessage);
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private static SerializedMessageToSend SerializeResponse(ResponseMessage responseToSend)
        {
            SerializedMessageToSend serMsg = new SerializedMessageToSend(responseToSend);
            SerializedMessageToSend? serMsgToDispose = serMsg;
            try
            {
                if (responseToSend.ActionResult is IActionResult actionResult)
                // Метод контроллера вернул специальный тип.
                {
                    var actionContext = new ActionContext(responseToSend.ActionMeta, serMsg.MemoryPoolBuffer);

                    // Сериализуем ответ.
                    actionResult.ExecuteResult(actionContext);
                    serMsg.StatusCode = actionContext.StatusCode;
                    serMsg.ContentEncoding = actionContext.ProducesEncoding;
                }
                else
                // Отправлять результат контроллера будем как есть.
                {
                    // Сериализуем ответ.
                    serMsg.StatusCode = StatusCode.Ok;

                    // Сериализуем контент если он есть (у void его нет).
                    if (responseToSend.ActionResult != null)
                    {
                        Debug.Assert(responseToSend.ActionMeta != null, "RAW результат может быть только на основе запроса");
                        responseToSend.ActionMeta.SerializerDelegate(serMsg.MemoryPoolBuffer, responseToSend.ActionResult);
                        serMsg.ContentEncoding = responseToSend.ActionMeta.ProducesEncoding;
                    }
                }
                serMsgToDispose = null; // Предотвратить Dispose.
                return serMsg;
            }
            finally
            {
                serMsgToDispose?.Dispose();
            }
        }

        /// <summary>
        /// Сериализует хэдер в стрим сообщения.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private static void AppendHeader(SerializedMessageToSend messageToSend)
        {
            HeaderDto header = CreateHeader(messageToSend);

            // Записать заголовок в конец стрима. Не бросает исключения.
            int headerSize = header.SerializeJson(messageToSend.MemoryPoolBuffer);

            // Запомним размер хэдера.
            messageToSend.HeaderSize = headerSize;
        }

        private static HeaderDto CreateHeader(SerializedMessageToSend messageToSend)
        {
            Debug.Assert(messageToSend != null);

            if (messageToSend.MessageToSend is ResponseMessage responseToSend)
            // Создать хедер ответа на запрос.
            {
                Debug.Assert(messageToSend.StatusCode != null, "StatusCode ответа не может быть Null");

                return new HeaderDto(responseToSend.Id, messageToSend.StatusCode.Value, messageToSend.MemoryPoolBuffer.WrittenCount, messageToSend.ContentEncoding);
            }
            else
            // Создать хедер для нового запроса.
            {
                var request = messageToSend.MessageToSend as RequestMethodMeta;
                Debug.Assert(request != null);

                return new HeaderDto(messageToSend.Uid, messageToSend.MemoryPoolBuffer.WrittenCount, messageToSend.ContentEncoding, request.MethodFullName);
            }
        }

        /// <summary>
        /// Добавляет хэдер и передает на отправку другому потоку если канал ещё не закрыт.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void TryPostMessage(SerializedMessageToSend serializedMessage)
        {
            Debug.Assert(serializedMessage != null);

            SerializedMessageToSend? toDispose = serializedMessage;
            try
            {
                // На текущем этапе сокет может быть уже уничтожен другим потоком.
                // В этом случае можем беспоследственно проигнорировать отправку; вызывающий получит исключение через RequestAwaiter.
                if (!IsDisposed)
                {
                    // Сериализуем хедер. Не бросает исключения.
                    AppendHeader(serializedMessage);

                    // Передать в очередь на отправку.
                    // (!) Из-за AllowSynchronousContinuations начнёт отправку текущим потоком если очередь пуста.
                    if (_sendChannel.Writer.TryWrite(serializedMessage))
                    // Успешно передали объект другому потоку.
                    {
                        // Мы больше не владеем этим объектом.
                        toDispose = null;
                    }
                }
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        private void TryPostMessage(IMessageToSend message)
        {
            // На текущем этапе сокет может быть уже уничтожен другим потоком.
            // В этом случае можем беспоследственно проигнорировать отправку; вызывающий получит исключение через RequestAwaiter.
            if (!IsDisposed)
            {
                // Передать в очередь на отправку.
                // (!) Из-за AllowSynchronousContinuations начнёт отправку текущим потоком если очередь пуста.
                if (_sendChannel.Writer.TryWrite(message))
                // Успешно передали объект другому потоку.
                {
                    // Мы больше не владеем этим объектом.
                }
            }
        }

        // Как и для ReceiveLoop здесь не должно быть глубокого async стека.
        /// <summary>
        /// Ждёт сообщения через очередь и отправляет в сокет.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private async Task LoopSendAsync() // Точка входа нового потока.
        {
            // Ждём сообщение для отправки.
            while (await _sendChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // Всегда true — у нас только один читатель.
                _sendChannel.Reader.TryRead(out IMessageToSend message);
                Debug.Assert(message != null);

                if (message is SerializedMessageToSend serializedMessage)
                {
                    // Теперь мы владеем этим объектом.
                    try
                    {
                        if (!IsDisposed) // Даже после Dispose мы должны опустошить очередь и сделать Dispose всем сообщениям.
                        {
                            //  Увеличить счетчик активных запросов.
                            if (TryIncreaseActiveRequestsCount(serializedMessage))
                            {
                                //LogSend(serializedMessage);

                                ReadOnlyMemory<byte> streamBuffer = serializedMessage.MemoryPoolBuffer.WrittenMemory;

                                // Размер сообщения без заголовка.
                                int messageSize = serializedMessage.MemoryPoolBuffer.WrittenCount - serializedMessage.HeaderSize;

                                if (serializedMessage.MessageToSend.TcpNoDelay != _tcpNoDelay)
                                {
                                    _ws.Socket.NoDelay = _tcpNoDelay = serializedMessage.MessageToSend.TcpNoDelay;
                                }

                                #region Отправка заголовка

                                try
                                {
                                    // Заголовок лежит в конце стрима.
                                    await SendBufferAsync(streamBuffer.Slice(messageSize, serializedMessage.HeaderSize), endOfMessage: true).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве.
                                    TryDispose(CloseReason.FromException(ex, _shutdownRequest));

                                    // Завершить поток.
                                    return;
                                }
                                #endregion

                                #region Отправка тела сообщения (запрос или ответ на запрос)

                                if (messageSize > 0)
                                {
                                    if (serializedMessage.Parts == null)
                                    {
                                        try
                                        {
                                            await SendBufferAsync(streamBuffer.Slice(0, messageSize), true).ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        // Обрыв соединения.
                                        {
                                            // Оповестить об обрыве.
                                            TryDispose(CloseReason.FromException(ex, _shutdownRequest));

                                            // Завершить поток.
                                            return;
                                        }
                                    }
                                    else
                                    {
                                        #region Отправка частями как Multipart

                                        ReadOnlyMemory<byte> segment = streamBuffer;
                                        for (int i = 0; i < serializedMessage.Parts.Length; i++)
                                        {
                                            Multipart part = serializedMessage.Parts[i];
                                            ReadOnlyMemory<byte> header = segment.Slice(part.ContentLength, part.HeaderLength);
                                            ReadOnlyMemory<byte> content = segment.Slice(0, part.ContentLength);
                                            bool lastPart = i == (serializedMessage.Parts.Length - 1);
                                            try
                                            {
                                                await SendBufferAsync(header, false).ConfigureAwait(false);
                                                await SendBufferAsync(content, lastPart).ConfigureAwait(false);
                                            }
                                            catch (Exception ex)
                                            // Обрыв соединения.
                                            {
                                                // Оповестить об обрыве.
                                                TryDispose(CloseReason.FromException(ex, _shutdownRequest));

                                                // Завершить поток.
                                                return;
                                            }
                                            // Перевести курсор на начало следующей части.
                                            segment = segment.Slice(content.Length + header.Length);
                                        }
                                        #endregion
                                    }
                                }
                                #endregion

                                // Уменьшить счетчик активных запросов.
                                if (!TryDecreaseActiveRequestsCount(serializedMessage))
                                // Пользователь запросил остановку сервиса.
                                {
                                    // Завершить поток.
                                    return;
                                }
                            }
                            else
                            // Пользователь запросил остановку сервиса.
                            {
                                // Завершить поток.
                                return;
                            }
                        }
                    }
                    finally
                    {
                        serializedMessage.Dispose();
                    }
                }
                else if (message is JsonRpcRequest jsonRequest)
                {
                    if (!IsDisposed) // Даже после Dispose мы должны опустошить очередь.
                    {
                        if (TrySerialize(jsonRequest, out ArrayBufferWriter<byte>? buffer))
                        {
                            try
                            {
                                //  Увеличить счетчик активных запросов.
                                if (IncreaseActiveRequestsCount())
                                {
                                    SetNoDelay(jsonRequest.MethodMeta);

                                    AssertJson(buffer.WrittenMemory.Span);
                                    try
                                    {
                                        await SendBufferAsync(buffer.WrittenMemory, Ms.WebSocketMessageType.Text, endOfMessage: true).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    // Обрыв соединения.
                                    {
                                        // Оповестить об обрыве.
                                        TryDispose(CloseReason.FromException(ex, _shutdownRequest));

                                        // Завершить поток.
                                        return;
                                    }

                                    // Уменьшить счетчик активных запросов.
                                    if (!DecreaseActiveRequestsCount())
                                    // Пользователь запросил остановку сервиса.
                                    {
                                        // Завершить поток.
                                        return;
                                    }
                                }
                                else
                                    return;
                            }
                            finally
                            {
                                buffer.Dispose();
                            }
                        }
                        else
                        {
                            // Не удалось сериализовать -> Игнорируем запрос, пользователь получит исключение.
                            continue;
                        }
                    }
                }
            }
        }

        [Conditional("DEBUG")]
        private static void AssertJson(ReadOnlySpan<byte> span)
        {
            var debugDisplayAsString = new DebuggerDisplayJson(span);
        }

        private void SetNoDelay(RequestMethodMeta methodMeta)
        {
            if (methodMeta.TcpNoDelay != _tcpNoDelay)
            {
                _ws.Socket.NoDelay = _tcpNoDelay = methodMeta.TcpNoDelay;
            }
        }

        private static bool TrySerialize(JsonRpcRequest jsonRequest, [NotNullWhen(true)] out ArrayBufferWriter<byte>? buffer)
        {
            try
            {
                buffer = jsonRequest.Serialize();
                return true;
            }
            catch (VRpcSerializationException ex)
            {
                jsonRequest.ResponseAwaiter.TrySetException(ex);
                buffer = default;
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="serializedMessage"></param>
        /// <returns>False если сервис требуется остановить.</returns>
        private bool TryDecreaseActiveRequestsCount(SerializedMessageToSend serializedMessage)
        {
            if (serializedMessage.MessageToSend.IsRequest)
            {
                return true;
            }
            else
            // Ответ успешно отправлен.
            {
                if (DecreaseActiveRequestsCount())
                {
                    return true;
                }
                else
                // Пользователь запросил остановку сервиса.
                {
                    // Не бросает исключения.
                    TryBeginSendCloseBeforeShutdown();

                    // Завершить поток.
                    return false;
                }
            }
        }

        /// <returns>False если сервис требуется остановить.</returns>
        private bool TryIncreaseActiveRequestsCount(SerializedMessageToSend serializedMessage)
        {
            if (serializedMessage.MessageToSend.IsRequest)
            // Происходит отправка запроса, а не ответа на запрос.
            {
                if (!serializedMessage.MessageToSend.IsNotificationRequest)
                // Должны получить ответ на этот запрос.
                {
                    if (IncreaseActiveRequestsCount())
                    {
                        return true;
                    }
                    else
                    // Пользователь запросил остановку сервиса.
                    {
                        // Просто завершить поток.
                        return false;
                    }
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask SendBufferAsync(ReadOnlyMemory<byte> buffer, bool endOfMessage)
        {
            Debug.Assert(!buffer.IsEmpty, "Протокол никогда не отправляет пустые сообщения");

            return _ws.SendAsync(buffer, Ms.WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ValueTask SendBufferAsync(ReadOnlyMemory<byte> buffer, Ms.WebSocketMessageType messageType, bool endOfMessage)
        {
            Debug.Assert(!buffer.IsEmpty, "Протокол никогда не отправляет пустые сообщения");

            return _ws.SendAsync(buffer, messageType, endOfMessage, CancellationToken.None);
        }

        //[Conditional("LOG_RPC")]
        //private static void LogSend(SerializedMessageToSend serializedMessage)
        //{
        //    byte[] streamBuffer = serializedMessage.MemPoolStream.GetBuffer();

        //    // Размер сообщения без заголовка.
        //    int contentSize = (int)serializedMessage.MemPoolStream.Length - serializedMessage.HeaderSize;

        //    var headerSpan = streamBuffer.AsSpan(contentSize, serializedMessage.HeaderSize);
        //    //var contentSpan = streamBuffer.AsSpan(0, contentSize);

        //    var header = HeaderDto.DeserializeProtoBuf(headerSpan.ToArray(), 0, headerSpan.Length);
        //    //string header = HeaderDto.DeserializeProtobuf(headerSpan.ToArray(), 0, headerSpan.Length).ToString();

        //    //string header = Encoding.UTF8.GetString(headerSpan.ToArray());
        //    //string content = Encoding.UTF8.GetString(contentSpan.ToArray());

        //    //header = Newtonsoft.Json.Linq.JToken.Parse(header).ToString(Newtonsoft.Json.Formatting.Indented);
        //    Debug.WriteLine(header);
        //    if (header != default)
        //    {
        //        if ((header.StatusCode == StatusCode.Ok || header.StatusCode == StatusCode.Request)
        //            && (serializedMessage.ContentEncoding == null || serializedMessage.ContentEncoding == "json"))
        //        {
        //            if (contentSize > 0)
        //            {
        //                try
        //                {
        //                    string content = System.Text.Json.JsonDocument.Parse(streamBuffer.AsMemory(0, contentSize)).RootElement.ToString();
        //                    Debug.WriteLine(content);
        //                }
        //                catch (Exception ex)
        //                {
        //                    Debug.WriteLine(ex);
        //                    if (Debugger.IsAttached)
        //                        Debugger.Break();
        //                }
        //            }
        //        }
        //        else
        //        {
        //            //Debug.WriteLine(streamBuffer.AsMemory(0, contentSize).Span.Select(x => x).ToString());
        //        }
        //    }
        //}

        /// <summary>
        /// Вызывает запрошенный метод контроллера и возвращает результат.
        /// Результатом может быть IActionResult или Raw объект или исключение.
        /// </summary>
        /// <exception cref="Exception">Исключение пользователя.</exception>
        /// <exception cref="ObjectDisposedException"/>
        /// <param name="receivedRequest">Гарантированно выполнит Dispose.</param>
        /// <returns><see cref="IActionResult"/> или любой объект.</returns>
        private ValueTask<object?> InvokeControllerAsync(in RequestContext receivedRequest)
        {
            bool requestToDispose = true;
            IServiceScope? scopeToDispose = null;
            try
            {
                // Проверить доступ к функции.
                if (ActionPermissionCheck(receivedRequest.ControllerMethod, out IActionResult? permissionError, out ClaimsPrincipal? user))
                {
                    IServiceScope scope = ServiceProvider.CreateScope();
                    scopeToDispose = scope;

                    // Инициализируем Scope текущим соединением.
                    var getProxyScope = scope.ServiceProvider.GetService<RequestContextScope>();
                    getProxyScope.ConnectionContext = this;

                    // Активируем контроллер через IoC.
                    var controller = scope.ServiceProvider.GetRequiredService(receivedRequest.ControllerMethod.ControllerType) as Controller;
                    Debug.Assert(controller != null);

                    // Подготавливаем контроллер.
                    controller.BeforeInvokeController(receivedRequest);
                    controller.BeforeInvokeController(this, user);

                    //BeforeInvokeController(controller);

                    // Вызов метода контроллера.
                    // (!) Результатом может быть не завершённый Task.
                    object? actionResult = receivedRequest.ControllerMethod.FastInvokeDelegate.Invoke(controller, receivedRequest.Args);

                    if (actionResult != null)
                    {
                        // Может бросить исключение.
                        ValueTask<object?> actionResultAsTask = DynamicAwaiter.ConvertToTask(actionResult);

                        if (actionResultAsTask.IsCompletedSuccessfully)
                        {
                            // Извлекаем результат из Task'а.
                            actionResult = actionResultAsTask.Result;

                            // Результат успешно получен без исключения.
                            return new ValueTask<object?>(actionResult);
                        }
                        else
                        // Будем ждать асинхронный результат.
                        {
                            // Предотвратить Dispose.
                            scopeToDispose = null;
                            requestToDispose = false;

                            return WaitForControllerActionAsync(actionResultAsTask, scope, receivedRequest);
                        }
                    }
                    else
                    {
                        return new ValueTask<object?>(result: null);
                    }
                }
                else
                // Нет доступа к методу контроллера.
                {
                    return new ValueTask<object?>(result: permissionError);
                }
            }
            finally
            {
                if (requestToDispose)
                    receivedRequest.Dispose();

                // ServiceScope выполнит Dispose всем созданным экземплярам.
                scopeToDispose?.Dispose();
            }

            static async ValueTask<object?> WaitForControllerActionAsync(ValueTask<object?> task, IServiceScope scope, RequestContext pendingRequest)
            {
                using (scope)
                using (pendingRequest)
                {
                    object? result = await task.ConfigureAwait(false);

                    // Результат успешно получен без исключения.
                    return result;
                }
            }
        }

        /// <summary>
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private protected abstract bool ActionPermissionCheck(ControllerMethodMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user);

        /// <summary>
        /// В новом потоке выполняет запрос и отправляет ему результат или ошибку.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void StartProcessRequest(in RequestContext request)
        {
#if NETSTANDARD2_0 || NET472
            ThreadPool.UnsafeQueueUserWorkItem(ProcessRequestThreadEntryPoint, state: Tuple.Create(this, request)); // Без замыкания.
#else
            // Предпочитаем глобальную очередь что-бы не замедлять читающий поток.
            ThreadPool.UnsafeQueueUserWorkItem(StaticProcessRequestThreadEntryPoint, state: Tuple.Create(this, request), preferLocal: false);
#endif
        }

#if NETSTANDARD2_0 || NET472
        private static void ProcessRequestThreadEntryPoint(object? state)
        {
            var tuple = state as Tuple<ManagedConnection, RequestContext>;
            Debug.Assert(tuple != null);
            tuple.Item1.ProcessRequestThreadEntryPoint(tuple.Item2);
        }  
#endif

        private static void StaticProcessRequestThreadEntryPoint(Tuple<ManagedConnection, RequestContext> state)
        {
            state.Item1.ProcessRequestThreadEntryPoint(state.Item2);
        }

        /// <summary>
        /// Выполняет запрос и отправляет результат или ошибку.
        /// </summary>
        /// <remarks>Точка входа потока из пула. Необработанные исключения пользователя крашнут процесс.</remarks>
        private void ProcessRequestThreadEntryPoint(RequestContext requestContext)
        {
            // Сокет может быть уже закрыт, например по таймауту,
            // в этом случае ничего выполнять не нужно.
            if (!IsDisposed)
            {
                ProcessRequest(in requestContext);
            }
            else
            {
                requestContext.Dispose();
            }
        }

        /// <summary>
        /// Увеличивает счётчик на 1 при получении запроса или при отправке запроса.
        /// </summary>
        /// <returns>False если был запрошен Shutdown и сервис нужно остановить.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IncreaseActiveRequestsCount()
        {
            // Увеличить счетчик запросов.
            if (Interlocked.Increment(ref _activeRequestCount) > 0)
            {
                return true;
            }
            else
            // Значение было -1, значит происходит остановка. Выполнять запрос не нужно.
            // Пользователь запросил остановку сервиса.
            {
                Debug.Assert(_shutdownRequest != null, "Нарушен счётчик запросов");

                return false;
            }
        }

        /// <summary>
        /// Уменьшает счётчик активных запросов на 1 при получении ответа на запрос или при отправке ответа на запрос.
        /// </summary>
        /// <returns>False если был запрошен Shutdown и сервис нужно остановить.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool DecreaseActiveRequestsCount()
        {
            // Получен ожидаемый ответ на запрос.
            if (Interlocked.Decrement(ref _activeRequestCount) != -1)
            {
                return true;
            }
            else
            // Значение было -1, значит происходит остановка. Выполнять запрос не нужно.
            // Пользователь запросил остановку сервиса.
            {
                Debug.Assert(_shutdownRequest != null, "Нарушен счётчик запросов");

                return false;
            }
        }

        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private void ProcessRequest(in RequestContext requestToInvoke)
        {
            if (requestToInvoke.IsResponseRequired)
            {
                Debug.Assert(requestToInvoke.Id != null);

                ValueTask<object?> pendingRequestTask;

                // Этот блок Try должен быть идентичен тому который чуть ниже — для асинхронной обработки.
                try
                {
                    // Выполняет запрос и возвращает результат.
                    // Может быть исключение пользователя.
                    pendingRequestTask = InvokeControllerAsync(in requestToInvoke);
                }
                catch (VRpcBadRequestException ex)
                {
                    // Вернуть результат с ошибкой.
                    SendBadRequest(in requestToInvoke, ex);
                    return;
                }
                catch (Exception)
                // Злая ошибка обработки запроса. Аналогично ошибке 500.
                {
                    // Прервать отладку.
                    //DebugOnly.Break();

                    // Вернуть результат с ошибкой.
                    SendInternalerverError(in requestToInvoke);
                    return;
                }

                if (pendingRequestTask.IsCompletedSuccessfully)
                // Результат контроллера получен синхронно.
                {
                    // Не бросает исключения.
                    object? actionResult = pendingRequestTask.Result;

                    SendOkResponse(in requestToInvoke, actionResult);
                }
                else
                // Результат контроллера — асинхронный таск.
                {
                    WaitResponseAndSendAsync(pendingRequestTask, requestToInvoke);

                    // TO THINK ошибки в таске можно обработать и не провоцируя исключения.
                    // ContinueWith должно быть в 5 раз быстрее. https://stackoverflow.com/questions/51923100/try-catchoperationcanceledexception-vs-continuewith
                    async void WaitResponseAndSendAsync(ValueTask<object?> task, RequestContext requestToInvoke)
                    {
                        Debug.Assert(requestToInvoke.Id != null);

                        object? actionResult;
                        // Этот блок Try должен быть идентичен тому который чуть выше — для синхронной обработки.
                        try
                        {
                            actionResult = await task.ConfigureAwait(false);
                        }
                        catch (VRpcBadRequestException ex)
                        {
                            // Вернуть результат с ошибкой.
                            SendBadRequest(in requestToInvoke, ex);
                            return;
                        }
                        catch (Exception)
                        // Злая ошибка обработки запроса. Аналогично ошибке 500.
                        {
                            // Прервать отладку.
                            //DebugOnly.Break();

                            // Вернуть результат с ошибкой.
                            SendInternalerverError(in requestToInvoke);
                            return;
                        }
                        SendOkResponse(in requestToInvoke, actionResult);
                    }
                }
            }
            else
            // Выполнить запрос без отправки ответа.
            {
                // Не бросает исключения.
                ProcessNotificationRequest(requestToInvoke);
            }
        }

        private void SendOkResponse(in RequestContext requestContext, object? actionResult)
        {
            Debug.Assert(requestContext.Id != null);

            SerializeResponseAndTrySend(new ResponseMessage(requestContext.Id.Value, requestContext.ControllerMethod, actionResult));
        }

        private void SendInternalerverError(in RequestContext requestToInvoke)
        {
            Debug.Assert(requestToInvoke.Id != null);

            // Вернуть результат с ошибкой.
            SerializeResponseAndTrySend(new ResponseMessage(requestToInvoke.Id.Value, requestToInvoke.ControllerMethod, new InternalErrorResult("Internal Server Error")));
        }

        private void SendBadRequest(in RequestContext requestToInvoke, VRpcBadRequestException exception)
        {
            Debug.Assert(requestToInvoke.IsResponseRequired);
            Debug.Assert(requestToInvoke.Id != null);

            var result = new BadRequestResult(exception.Message);
            var response = new ResponseMessage(requestToInvoke.Id.Value, requestToInvoke.ControllerMethod, result);

            // Вернуть результат с ошибкой.
            SerializeResponseAndTrySend(response);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void ProcessNotificationRequest(in RequestContext notificationRequest)
        {
            Debug.Assert(notificationRequest.IsResponseRequired == false);

            ValueTask<object?> pendingRequestTask;
            try
            {
                // Может быть исключение пользователя.
                pendingRequestTask = InvokeControllerAsync(in notificationRequest);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            // Исключение пользователя.
            {
                DebugOnly.Break();
                Debug.WriteLine(ex);
                return;
            }

            if (!pendingRequestTask.IsCompletedSuccessfully)
            {
                WaitForNotification(pendingRequestTask);
            }

            static async void WaitForNotification(ValueTask<object?> t)
            {
                try
                {
                    await t.ConfigureAwait(false);
                }
                catch (Exception ex)
                // Злая ошибка обработки запроса. Аналогично ошибке 500.
                {
                    DebugOnly.Break();
                    Debug.WriteLine(ex);
                }
            }
        }

        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private void SerializeResponseAndTrySend(ResponseMessage responseMessage)
        {
            SerializedMessageToSend responseToSend = SerializeResponse(responseMessage);

            // Не бросает исключения.
            TryPostMessage(responseToSend);
        }

        /// <summary>
        /// Потокобезопасно освобождает ресурсы соединения. Вызывается при закрытии соединения.
        /// Взводит <see cref="Completion"/> и оповещает все ожидающие потоки.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        /// <param name="possibleReason">Одна из возможных причин обрыва соединения.</param>
        private void TryDispose(CloseReason possibleReason)
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            // Только один поток может зайти сюда (за всю жизнь экземпляра).
            // Это настоящая причина обрыва соединения.
            {
                // Лучше выполнить в первую очередь.
                _sendChannel.Writer.TryComplete();

                // Передать исключение всем ожидающим потокам.
                _responseAwaiters.TryPropagateExceptionAndLockup(possibleReason.ToException());

                // Закрыть соединение.
                _ws.Dispose();

                // Синхронизироваться с подписчиками на событие Disconnected.
                EventHandler<SocketDisconnectedEventArgs>? disconnected;
                lock (DisconnectEventObj)
                {
                    // Запомнить истинную причину обрыва.
                    DisconnectReason = possibleReason;

                    // Установить флаг после причины обрыва.
                    _isConnected = false;

                    // Скопируем делегат что-бы вызывать его не в блокировке — на всякий случай.
                    disconnected = _disconnected;

                    // Теперь можно безопасно убрать подписчиков — никто больше не сможет подписаться.
                    _disconnected = null;
                }

                try
                {
                    // Сообщить об обрыве.
                    disconnected?.Invoke(this, new SocketDisconnectedEventArgs(this, possibleReason));
                }
                catch (Exception ex)
                // Нужна защита от пользовательских ошибок в обработчике события.
                {
                    // Нужно проглотить исключение потому что его некому обработать.
                    Debug.Fail($"Exception occurred in {nameof(Disconnected)} event handler", ex.ToString());
                }

                // Установить Task Completion.
                TrySetCompletion(possibleReason);
            }
        }

        /// <remarks>Не бросает исключения.</remarks>
        private void TrySetCompletion(CloseReason closeReason)
        {
            // Установить Task Completion.
            if (_completionTcs.TrySetResult(closeReason))
            {
                try
                {
                    _cts.Cancel(false);
                }
                catch (AggregateException ex)
                // Нужна защита от пользовательских ошибок в токене отмены.
                {
                    // Нужно проглотить исключение потому что его некому обработать.
                    Debug.Fail("Exception occurred on " + nameof(CompletionToken) + ".Cancel(false)", ex.ToString());
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        T IGetProxy.GetProxy<T>() where T : class => InnerGetProxy<T>();

        private protected abstract T InnerGetProxy<T>() where T : class;

        protected virtual void DisposeManaged()
        {
            TryDispose(CloseReason.FromException(new ObjectDisposedException(GetType().FullName), _shutdownRequest, "Пользователь вызвал Dispose."));
        }

        /// <summary>
        /// Потокобезопасно закрывает соединение и освобождает все ресурсы.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeManaged();
            }
        }
    }
}
