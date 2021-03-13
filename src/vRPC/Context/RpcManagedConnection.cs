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
using DanilovSoft.vRPC.Source;
using System.IO;
using DanilovSoft.vRPC.Context;
using System.Text.Json;
using System.ComponentModel;
using ProtoBuf;
using DanilovSoft.vRPC.Decorator;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Контекст соединения Web-Сокета. Владеет соединением.
    /// </summary>
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public abstract class RpcManagedConnection : IDisposable, /*IGetProxy,*/ IThreadPoolWorkItem
    {
        private readonly ProxyCache _proxyCache = new();
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly InvokeActionsDictionary _invokeMethods;
        /// <summary>
        /// Для Completion.
        /// </summary>
        private readonly TaskCompletionSource<CloseReason> _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>
        /// Взводится при обрыве соединения.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2213", Justification = "Не требует вызывать Dispose если гарантированно будет вызван Cancel")]
        private readonly CancellationTokenSource _cts = new();
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
        public event EventHandler<Exception>? NotificationError;
        internal bool IsServer { get; }
        public IServiceProvider ServiceProvider { get; }
        /// <summary>
        /// Подключенный TCP сокет.
        /// </summary>
        private readonly ManagedWebSocket _ws;
        /// <summary>
        /// Коллекция запросов ожидающие ответ от удалённой стороны.
        /// </summary>
        private readonly PendingRequestDictionary _pendingRequests;
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
        /// <summary>
        /// Потоки могут арендровать этот экземпляр, по очереди.
        /// </summary>
        private ReusableJNotification? _reusableJNotification = new();
        private ReusableVNotification? _reusableVNotification = new();
        private ReusableJRequest? _reusableJRequest;
        private ReusableVRequest? _reusableVRequest;
        private RequestContext? _reusableContext;

        // ctor.
        /// <summary>
        /// Принимает открытое соединение Web-Socket.
        /// </summary>
        internal RpcManagedConnection(ManagedWebSocket webSocket, bool isServer, IServiceProvider serviceProvider, InvokeActionsDictionary actions)
        {
            IsServer = isServer;

            Debug.Assert(webSocket.State == Ms.WebSocketState.Open);

            LocalEndPoint = webSocket.LocalEndPoint;
            RemoteEndPoint = webSocket.RemoteEndPoint;
            _ws = webSocket;
            _tcpNoDelay = webSocket.Socket?.NoDelay ?? false;
            //_pipe = new Pipe();

            _pendingRequests = new PendingRequestDictionary();

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

            _reusableJRequest = new ReusableJRequest(this);
            _reusableContext = new RequestContext(this);
            _reusableVRequest = new ReusableVRequest(this);
        }

        /// <summary>
        /// Запускает бесконечный цикл обработки запросов.
        /// </summary>
        internal void StartReceiveSendLoop()
        {
            // Не бросает исключения.
            _senderTask = SendLoop();

#if NETSTANDARD2_0 || NET472
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, state: this);

            static void ReceiveLoopStart(object? state)
            {
                var self = state as RpcManagedConnection;
                self.ReceiveLoop();
            }
#else
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false); // Через глобальную очередь.
#endif
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
                var vException = new VRpcException(e.DisconnectingReason.Error.Message, e.DisconnectingReason.Error);

                closeReason = CloseReason.FromException(vException, _shutdownRequest, e.DisconnectingReason.AdditionalDescription);
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

                    if (!TryDecreaseActiveRequestsCount())
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
                    TryDispose(CloseReason.FromException(new VRpcException("Обрыв при отправке Close.", ex), _shutdownRequest));

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

        #region Отправка запроса

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны сервера.</remarks>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal ValueTask OnServerNotificationCall(RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(method.IsNotificationRequest);

            return CreateAndSendNotification(method, args);
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны сервера.</remarks>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Task<TResult?> OnServerRequestCall<TResult>(VRequest<TResult> vRequest)
        {
            Debug.Assert(vRequest.Method.ReturnType == typeof(TResult));
            Debug.Assert(!vRequest.Method.IsNotificationRequest);

            if (TrySendRequest<TResult>(vRequest, out var errorTask))
            {
                return vRequest.Task;
            }
            else
                return errorTask;
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны клиента.</remarks>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        /// <exception cref="Exception">Могут быть исключения не инкапсулированные в Task.</exception>
        internal static Task<TResult?> OnClientRequestCall<TResult>(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(!method.IsNotificationRequest);

            if (connectionTask.IsCompleted)
            {
                try
                {
                    // Может быть исключение если не удалось подключиться.
                    // у ValueTask можно обращаться к Result.
                    ClientSideConnection connection = connectionTask.Result;
                    return connection.SendRequest<TResult>(method, args);
                }
                catch (Exception ex)
                {
                    return Task.FromException<TResult?>(ex);
                }
            }
            else
            {
                return WaitAsync(connectionTask, method, args).Unwrap();

                static async Task<Task<TResult?>> WaitAsync(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta method, object?[] args)
                {
                    var connection = await connectionTask.ConfigureAwait(false);
                    var task = connection.SendRequest<TResult>(method, args);
                    return task;
                }
            }
        }

        private Task<TResult?> SendRequest<TResult>(RequestMethodMeta method, object?[] args)
        {
            if (method.IsJsonRpc)
            {
                ReusableJRequest? reusableRequest = Interlocked.Exchange(ref _reusableJRequest, null);
                if (reusableRequest != null)
                {
                    var task = reusableRequest.Initialize<TResult>(method, args);

                    // Не бросает исключения.
                    if (TrySendRequest<TResult>(reusableRequest, out var errorTask))
                    {
                        return task;
                    }
                    else
                        return errorTask;
                }
                else
                {
                    Debug.WriteLine("Не удалось переиспользовать объект запроса и был создан новый.");

                    var request = new JRequest<TResult>(method, args);

                    if (TrySendRequest<TResult>(request, out var errorTask))
                    {
                        return request.Task;
                    }
                    else
                        return errorTask;
                }
            }
            else
            {
                ReusableVRequest? reusableRequest = Interlocked.Exchange(ref _reusableVRequest, null);
                if (reusableRequest != null)
                {
                    var task = reusableRequest.Initialize<TResult>(method, args);

                    if (TrySendRequest<TResult>(reusableRequest, out var errorTask))
                    {
                        return task;
                    }
                    else
                        return errorTask;
                }
                else
                {
                    Debug.WriteLine("Не удалось переиспользовать объект запроса и был создан новый.");

                    var request = new VRequest<TResult>(method, args);

                    if (TrySendRequest<TResult>(request, out var errorTask))
                    {
                        return request.Task;
                    }
                    else
                        return errorTask;
                }
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <remarks>Со стороны клиента.</remarks>
        /// <exception cref="Exception">Могут быть исключения не инкапсулированные в Task.</exception>
        internal static ValueTask OnClientNotificationCall(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta method, object?[] args)
        {
            Debug.Assert(method.IsNotificationRequest);

            if (connectionTask.IsCompleted)
            {
                // Может бросить исключение.
                RpcManagedConnection connection = connectionTask.Result; // у ValueTask можно обращаться к Result.

                // Отправляет уведомление через очередь.
                return connection.CreateAndSendNotification(method, args);
            }
            else
            // Подключение к серверу ещё не завершено.
            {
                return WaitAsync(connectionTask, method, args);
            }

            // Локальная функция.
            static async ValueTask WaitAsync(ValueTask<ClientSideConnection> connectionTask, RequestMethodMeta method, object?[] args)
            {
                ClientSideConnection connection = await connectionTask.ConfigureAwait(false);

                await connection.CreateAndSendNotification(method, args).ConfigureAwait(false);
            }
        }

        private ValueTask CreateAndSendNotification(RequestMethodMeta method, object?[] args)
        {
            var notification = CreateNotification(method, args);

            // Отправляет запрос и получает результат от удалённой стороны.
            return SendNotification(notification);
        }

        private INotification CreateNotification(RequestMethodMeta method, object?[] args)
        {
            if (method.IsJsonRpc)
            {
                var notification = Interlocked.Exchange(ref _reusableJNotification, null);
                if (notification != null)
                // Успешно арендовали объект.
                {
                    notification.Initialize(method, args);
                    return notification;
                }
                else
                {
                    return new JNotification(method, args);
                }
            }
            else
            {
                var notification = Interlocked.Exchange(ref _reusableVNotification, null);
                if (notification != null)
                // Успешно арендовали объект.
                {
                    notification.Initialize(method, args);
                    return notification;
                }
                else
                {
                    return new VNotification(method, args);
                }
            }
        }

        /// <summary>
        /// Отправляет запрос-уведомление через очередь (выполнит отправку текущим потоком если очередь пуста).
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal ValueTask SendNotification(INotification notification)
        {
            if (_shutdownRequest == null)
            {
                if (!IsDisposed)
                {
                    TryPostMessage(notification);

                    return notification.WaitNotificationAsync();
                }
                else
                    return new(Task.FromException(new ObjectDisposedException(GetType().FullName)));
            }
            else
                return new(Task.FromException(new VRpcShutdownException(_shutdownRequest)));
        }

        internal void AtomicRestoreReusableJ(ReusableJRequest reusable)
        {
            Debug.Assert(_reusableJRequest == null);

            Volatile.Write(ref _reusableJRequest, reusable);
        }

        /// <summary>
        /// Записывает ссылку в глобальную переменную вместо Null, делая объект доступным для переиспользования.
        /// </summary>
        internal void AtomicRestoreReusableV(ReusableVRequest reusable)
        {
            Debug.Assert(_reusableVRequest == null);

            Volatile.Write(ref _reusableVRequest, reusable);
        }

        #endregion

        /// <summary>
        /// Отправляет запрос и ожидает ответ.
        /// Передаёт владение объектом <paramref name="request"/> другому потоку.
        /// </summary>
        /// <param name="errorTask">Может быть <see cref="ObjectDisposedException"/></param>
        /// <remarks>Происходит при обращении к прокси-интерфейсу. Не бросает исключения.</remarks>
        /// <returns>Таск с результатом от сервера или с исключением.</returns>
        private protected bool TrySendRequest<TResult>(IResponseAwaiter request, [NotNullWhen(false)] out Task<TResult?>? errorTask)
        {
            // Shutdown нужно проверять раньше чем Dispose потому что Dispose может быть по причине Shutdown.
            if (_shutdownRequest == null) // volatile проверка.
            {
                if (!IsDisposed) // volatile проверка.
                {
                    int id;
                    try
                    {
                        // Добавить запрос в словарь для последующей связки с ответом.
                        _pendingRequests.Add(request, out id);
                    }
                    catch (Exception ex)
                    {
                        errorTask = Task.FromException<TResult?>(ex);
                        return false;
                    }

                    // Назначить запросу уникальный идентификатор.
                    request.Id = id;

                    // Планируем отправку запроса.
                    TryPostMessage(request);

                    errorTask = default;
                    return true;
                }
                else
                {
                    errorTask = Task.FromException<TResult?>(new ObjectDisposedException(GetType().FullName));
                    return false;
                }
            }
            else
            {
                errorTask = Task.FromException<TResult?>(new VRpcShutdownException(_shutdownRequest));
                return false;
            }
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
                            TryDispose(CloseReason.FromException(new VRpcException("Обрыв соединения в потоке чтения сообщений.", ex), _shutdownRequest));
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

                if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Binary)
                {
                    HeaderDto header = HandleRpcMessage(headerBuffer.AsSpan(0, webSocketMessage.Count), out Task? errorTask);

                    if (errorTask == null)
                    {
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
                                            TryDispose(CloseReason.FromException(new VRpcException("Обрыв при чтении контента сообщения.", ex), _shutdownRequest));
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
                    else
                    {
                        await errorTask.ConfigureAwait(false);
                        return;
                    }
                }
                else if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Text)
                // Получен Json-rpc запрос или ответ.
                {
                    if (HandleJrpcMessage(headerBuffer.AsSpan(0, webSocketMessage.Count), out Task? taskToWait))
                    {
                        continue;
                    }
                    else
                    {
                        if (taskToWait != null)
                            await taskToWait.ConfigureAwait(false);

                        // Завершить поток.
                        return;
                    }
                }
                else
                // Получен Close.
                {
                    await TryNonBinaryTypeCloseAndDisposeAsync(webSocketMessage).ConfigureAwait(false);
                    return;
                }
                #endregion
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private HeaderDto HandleRpcMessage(ReadOnlySpan<byte> buffer, out Task? errorTask)
        {
            try
            {
                var header = CustomVSerializer.DeserializeHeader(buffer);
                header.Assert();
                errorTask = null;
                return header;
            }
            catch (Exception serializerException)
            // Не удалось десериализовать заголовок.
            {
                // Отправка Close и завершить поток.
                errorTask = SendCloseHeaderErrorAsync(serializerException);
                return default;
            }
        }

        // Получен запрос или ответ json-rpc.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HandleJrpcMessage(ReadOnlySpan<byte> buffer, [MaybeNullWhen(true)] out Task? taskToWait)
        {
            bool result;
            IMessageToSend? error;
            try
            {
                result = DeserializeJsonRpcMessage(buffer, out error);
                taskToWait = null;
            }
            catch (JsonException ex)
            // Ошибка при десериализации полученного Json.
            {
                // Отправка Close с сообщением.
                taskToWait = SendCloseParseErrorAsync(ex);
                return false; // Нужно завершить поток.
            }

            if (error != null)
            {
                TryPostMessage(error);
            }
            return result;
        }

        /// <summary>Разбор полученного сообщения. Сообщение может быть запросом или ответом на запрос.</summary>
        /// <exception cref="JsonException"/>
        /// <returns>False если нужно завершить поток.</returns>
        internal bool DeserializeJsonRpcMessage(ReadOnlySpan<byte> utf8Json, [MaybeNullWhen(true)] out IMessageToSend? errorResponse)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(utf8Json);
#endif
            int? id = null;
            string? methodName = null;
            ControllerMethodMeta? method = null;
            object?[]? args = null;
            IActionResult? paramsError = null;
            JRpcErrorModel errorModel = default;

            // Если параметры в Json записаны раньше чем Id то придётся перематывать.
            JsonReaderState paramsState;

            var reader = new Utf8JsonReader(utf8Json);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (method == default && reader.ValueTextEquals(JsonRpcSerializer.Method.EncodedUtf8Bytes))
                    {
                        if (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                methodName = reader.GetString();
                                if (methodName != null)
                                {
                                    if (_invokeMethods.TryGetAction(methodName, out method))
                                    {
                                        args = method.PrepareArgs();
                                    }
                                }
                            }
                        }
                    }
                    else if (reader.ValueTextEquals(JsonRpcSerializer.Params.EncodedUtf8Bytes))
                    {
                        if (method != default)
                        {
                            Debug.Assert(args != null, "Если нашли метод, то и параметры уже инициализировали.");

                            // В случае ошибки нужно продолжить искать айдишник запроса.
                            TryParseJsonArgs(method, args, reader, out paramsError);
                        }
                        else if (methodName == null)
                        {
                            // TODO найти метод и перечитать параметры.
                            paramsState = reader.CurrentState;
                        }
                    }
                    else if (id == null && reader.ValueTextEquals(JsonRpcSerializer.Id.EncodedUtf8Bytes))
                    {
                        if (reader.Read())
                        {
                            // TODO формат может быть String или Number.
                            if (reader.TokenType == JsonTokenType.Number)
                            {
                                id = reader.GetInt32();
                            }
                            else if (reader.TokenType == JsonTokenType.String)
                            {
                                if (int.TryParse(reader.GetString(), out int result))
                                {
                                    id = result;
                                }
                                else
                                // Мы не поддерживаем произвольный формат айдишников что-бы уменьшить аллокацию.
                                {
                                    // TODO
                                }
                            }
                        }
                    }
                    else if (reader.ValueTextEquals(JsonRpcSerializer.Result.EncodedUtf8Bytes))
                    // Теперь мы точно знаем что это ответ на запрос.
                    {
                        if (id != null)
                        {
                            if (reader.Read())
                            {
                                if (_pendingRequests.TryRemove(id.Value, out IResponseAwaiter? jRequest))
                                {
                                    // Десериализовать ответ в тип возврата.
                                    jRequest.TrySetJResponse(ref reader);

                                    errorResponse = null;

                                    // Получен ожидаемый ответ на запрос.
                                    if (DecreaseActiveRequestsCountOrClose())
                                    {
                                        return true;
                                    }
                                    else
                                    // Пользователь запросил остановку сервиса.
                                    {
                                        // Завершить поток.
                                        return false;
                                    }
                                }
                                else
                                // Получили бесполезное сообщение.
                                {
                                    Debug.Assert(false);
                                }
                            }
                        }
                    }
                    else if (errorModel == default && reader.ValueTextEquals(JsonRpcSerializer.Error.EncodedUtf8Bytes))
                    // Это ответ на запрос.
                    {
                        if (reader.Read())
                        {
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                            {
                                if (reader.ValueTextEquals(JsonRpcSerializer.Code.EncodedUtf8Bytes))
                                {
                                    if (reader.Read() && reader.TokenType == JsonTokenType.Number)
                                    {
                                        errorModel.Code = (StatusCode)reader.GetInt32();
                                    }
                                }
                                else if (reader.ValueTextEquals(JsonRpcSerializer.Message.EncodedUtf8Bytes))
                                {
                                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                                    {
                                        errorModel.Message = reader.GetString();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (id != null)
            // Запрос или ответ на запрос.
            {
                if (method != null)
                // Запрос.
                {
                    if (paramsError == null)
                    {
                        // Атомарно запускаем запрос.
                        if (TryIncreaseActiveRequestsCount(isResponseRequired: true))
                        {
                            Debug.Assert(args != null);

                            CreateRequestContextAndStart(id, method, args, isJsonRpc: true);

                            errorResponse = null;
                            return true;
                        }
                        else
                        // Происходит остановка. Выполнять запрос не нужно.
                        {
                            errorResponse = null;
                            return false; // Завершить поток чтения.
                        }
                    }
                    else
                    // Неудалось разобрать параметры запроса.
                    {
                        errorResponse = new JErrorResponse(id.Value, paramsError);
                        return true;
                    }
                }
                else
                // Отсутствует метод -> может быть ответ на запрос.
                {
                    if (errorModel != default)
                    // Ответ на запрос где результат — ошибка.
                    {
                        if (_pendingRequests.TryRemove(id.Value, out IResponseAwaiter? pendingRequest))
                        {
                            // На основе кода и сообщения можно создать исключение.
                            VRpcException exception = ExceptionHelper.ToException(errorModel.Code, errorModel.Message);
                            pendingRequest.TrySetErrorResponse(exception);
                        }
                        else
                        {
                            Debug.Assert(false, "Получен ответ которого мы не ждали.");
                        }
                        errorResponse = null;
                        return true;
                    }
                    else if (methodName != null)
                    // Получен запрос но метод не найден.
                    {
                        if (TryIncreaseActiveRequestsCount(isResponseRequired: true))
                        {
                            // Передать на отправку результат с ошибкой через очередь.
                            errorResponse = new JErrorResponse(id.Value, new MethodNotFoundResult(methodName, "Method not found"));
                            return true;
                        }
                        else
                        // Происходит остановка. Выполнять запрос не нужно.
                        {
                            errorResponse = null;
                            return false;
                        }
                    }
                    else
                    // Не валидное сообщение.
                    {
                        errorResponse = new JErrorResponse(id.Value, new InvalidRequestResult());
                        return true; // Продолжить получение сообщений.
                    }
                }
            }
            else
            // id отсутствует — может быть нотификация или сообщение об ошибке.
            {
                // {"jsonrpc": "2.0", "method": "update", "params": [1,2,3,4,5]}

                if (method != null)
                // Нотификация.
                {
                    if (paramsError == null)
                    {
                        Debug.Assert(args != null);

                        CreateRequestContextAndStart(id: null, method, args, isJsonRpc: true);

                        errorResponse = null;
                        return true;
                    }
                    else
                    {
                        errorResponse = new JErrorResponse(id: null, paramsError);
                        return true; // Продолжить получение сообщений.
                    }
                }
                else if (errorModel != default)
                // Ошибка без айдишника.
                {
                    Debug.Assert(false, "NotImplemented");
                    errorResponse = null;
                    return true;
                }
                else
                // Получено невалидное сообщение.
                {
                    errorResponse = new JErrorResponse(id: null, new InvalidRequestResult());
                    return true; // Продолжить получение сообщений.
                }
            }
        }

        /// <returns>True если параметры успешно десериализованы.</returns>
        private static bool TryParseJsonArgs(ControllerMethodMeta method, object?[] args, Utf8JsonReader reader, [MaybeNullWhen(true)] out IActionResult? error)
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
                            Type paramExpectedType = method.Parametergs[argsInJsonCounter].ParameterType;
                            try
                            {
                                args[argsInJsonCounter] = JsonSerializer.Deserialize(ref reader, paramExpectedType);
                            }
                            catch (JsonException)
                            {
                                error = ResponseHelper.JErrorDeserializingArgument(method.MethodFullName, argIndex: argsInJsonCounter, paramExpectedType);
                                return false;
                            }
                            argsInJsonCounter++;
                        }
                        else
                        // Несоответствие числа параметров.
                        {
                            error = ResponseHelper.JArgumentsCountMismatchError(method.MethodFullName, method.Parametergs.Length);
                            return false;
                        }
                    }
                }
            }
            error = null;
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
                            #region Десериализация запроса

                            if (CustomVSerializer.TryDeserializeArgs(payload, method, in header, out object[]? args, out IActionResult? error))
                            {
                                // Начать выполнение запроса в отдельном потоке.
                                CreateRequestContextAndStart(header.Id, method, args, isJsonRpc: false);
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
                                    TryPostMessage(new VErrorResponse(header.Id.Value, error));
                                }

                                // Вернуться к чтению следующего сообщения.
                                return true;

                                #endregion
                            }
                            #endregion
                        }
                        else
                        // Запрашиваемый метод не найден -> был отправлен MethodNotFoundResult.
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
                if (header.Id != null && _pendingRequests.TryRemove(header.Id.Value, out IResponseAwaiter? vRequest))
                // Передать ответ ожидающему потоку.
                {
                    vRequest.TrySetVResponse(in header, payload);

                    // Получен ожидаемый ответ на запрос.
                    return DecreaseActiveRequestsCountOrClose();
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

                    MethodNotFoundResult error = ResponseHelper.MethodNotFound(header.MethodName);

                    // Передать на отправку результат с ошибкой через очередь.
                    TryPostMessage(new VErrorResponse(header.Id.Value, error));
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
                TryPostMessage(new VErrorResponse(header.Id.Value, new InvalidRequestResult("В заголовке запроса отсутствует имя метода")));

                return false;
            }
        }

        /// <summary>
        /// Увеличивает счётчик активных запросов.
        /// </summary>
        /// <returns>true если разрешено продолжить выполнение запроса, 
        /// false если требуется остановить сервис.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryIncreaseActiveRequestsCount(bool isResponseRequired)
        {
            if (isResponseRequired)
            {
                if (TryIncreaseActiveRequestsCount())
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
        private async Task TryFinishSenderAndSendCloseAsync()
        {
            if (await TryCompleteSenderAsync().ConfigureAwait(false))
            {
                try
                {
                    await _ws.CloseOutputAsync(_ws.CloseStatus!.Value, _ws.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                // Обрыв соединения.
                {
                    // Оповестить об обрыве и завершить поток.
                    TryDispose(CloseReason.FromException(new VRpcException("Обрыв при отправке Close.", ex), _shutdownRequest));
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

        /// <summary>
        /// Отправляет Close с сообщением и распространяет исключение <see cref="VRpcProtocolErrorException"/> ожидающим потокам.
        /// </summary>
        /// <remarks>Для Json-RPC</remarks>
        private Task SendCloseParseErrorAsync(JsonException parseException)
        {
            var propagateException = new VRpcProtocolErrorException(SR2.GetString(SR.JsonRpcProtocolError, parseException.Message), innerException: parseException);

            // Отправка Close.
            return TryCloseAndDisposeAsync(propagateException, "Parse error (-32700)");
        }

        private Task SendCloseHeaderErrorAsync(Exception serializerException)
        {
            // Отправка Close.
            var propagateException = new VRpcProtocolErrorException(SR2.GetString(SR.ProtocolError, serializerException.Message), serializerException);
            return TryCloseAndDisposeAsync(propagateException, $"Unable to deserialize header.");
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
            _pendingRequests.TryPropagateExceptionAndLockup(protocolErrorException);

            if (await TryCompleteSenderAsync().ConfigureAwait(false))
            {
                try
                {
                    // Отключаемся от сокета с небольшим таймаутом.
                    using var cts = new CancellationTokenSource(1_000);
                    await _ws.CloseAsync(Ms.WebSocketCloseStatus.ProtocolError, closeDescription, cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                // Злой обрыв соединения.
                {
                    // Оповестить об обрыве.
                    TryDispose(CloseReason.FromException(new VRpcException("Обрыв при отправке Close.", ex), _shutdownRequest));

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
        /// Передает на отправку другому потоку если канал ещё не закрыт.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void TryPostMessage(IMessageToSend message)
        {
            // На текущем этапе сокет может быть уже уничтожен другим потоком.
            // В этом случае можем беспоследственно проигнорировать отправку; вызывающий получит исключение через RequestAwaiter.
            if (!IsDisposed)
            {
                // Сериализуем хедер. Не бросает исключения.
                //AppendHeader(serializedMessage);

                // Передать в очередь на отправку.
                // (!) Из-за AllowSynchronousContinuations начнёт отправку текущим потоком если очередь пуста.
                _sendChannel.Writer.TryWrite(message);
            }
        }

        // Как и для ReceiveLoop здесь не должно быть глубокого async стека.
        /// <summary>
        /// Ждёт сообщения через очередь и отправляет в сокет.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private async Task SendLoop() // Точка входа нового потока.
        {
            // Ждём сообщение для отправки.
            while (await _sendChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // Всегда true — у нас только один читатель.
                _sendChannel.Reader.TryRead(out IMessageToSend? message);
                Debug.Assert(message != null);

                if (!IsDisposed) // Даже после Dispose мы должны опустошить очередь и сделать Dispose всем сообщениям.
                {
                    if (message is RequestContext response)
                    // Ответ на основе результата контроллера.
                    {
                        Debug.Assert(response.Method != null);

                        if (response.IsJsonRpcRequest)
                        {
                            ArrayBufferWriter<byte> buffer = response.SerializeResponseAsJrpc();
                            try
                            {
                                SetTcpNoDelay(response.Method.TcpNoDelay);

                                // Нужно освободить ресурс перед отправкой сообщения.
                                ReleaseReusable(response);

                                DebugDisplayJson(buffer.WrittenMemory.Span);
                                try
                                {
                                    await SendBufferAsync(buffer.WrittenMemory, Ms.WebSocketMessageType.Text, endOfMessage: true).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве.
                                    TryDispose(CloseReason.FromException(new VRpcException(SR.SenderLoopError, ex), _shutdownRequest));

                                    // Завершить поток.
                                    return;
                                }
                            }
                            finally
                            {
                                buffer.Return();
                            }
                        }
                        else
                        {
                            int headerSize;
                            ArrayBufferWriter<byte> buffer;
                            try
                            {
                                buffer = response.SerializeResponseAsVrpc(out headerSize);
                            }
                            catch (JsonException)
                            {
                                response.Result = new InternalErrorResult("Не удалось сериализовать ответ в json.");
                                buffer = response.SerializeResponseAsVrpc(out headerSize);
                            }
                            catch (ProtoException)
                            {
                                response.Result = new InternalErrorResult("Не удалось сериализовать ответ в proto-buf.");
                                buffer = response.SerializeResponseAsVrpc(out headerSize);
                            }

                            try
                            {
                                // Размер сообщения без заголовка.
                                int messageSize = buffer.WrittenCount - headerSize;

                                SetTcpNoDelay(response.Method.TcpNoDelay);

                                // Нужно освободить ресурс перед отправкой сообщения.
                                ReleaseReusable(response);

                                #region Отправка заголовка

                                try
                                {
                                    // Заголовок лежит в конце стрима.
                                    await SendBufferAsync(buffer.WrittenMemory.Slice(messageSize, headerSize), endOfMessage: true).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве.
                                    TryDispose(CloseReason.FromException(new VRpcException(SR.SenderLoopError, ex), _shutdownRequest));

                                    // Завершить поток.
                                    return;
                                }
                                #endregion

                                #region Отправка тела сообщения (запрос или ответ на запрос)

                                if (messageSize > 0)
                                {
                                    try
                                    {
                                        await SendBufferAsync(buffer.WrittenMemory.Slice(0, messageSize), true).ConfigureAwait(false);
                                    }
                                    catch (Exception ex)
                                    // Обрыв соединения.
                                    {
                                        // Оповестить об обрыве.
                                        TryDispose(CloseReason.FromException(new VRpcException(SR.SenderLoopError, ex), _shutdownRequest));

                                        // Завершить поток.
                                        return;
                                    }
                                }
                                #endregion
                            }
                            finally
                            {
                                buffer.Return();
                            }
                        }

                        // Уменьшить счетчик активных запросов.
                        if (DecreaseActiveRequestsCountOrClose())
                        {
                            continue;
                        }
                        else
                        // Пользователь запросил остановку сервиса.
                        {
                            // Завершить поток.
                            return;
                        }
                    }
                    else if (message is IVRequest vRequest)
                    // Отправляем запрос или нотификацию.
                    {
                        if (vRequest.TryBeginSend())
                        {
                            // Если не удалось сериализовать -> игнорируем отправку, пользователь получит исключение.
                            // Переводит состояние сообщения в GotErrorResponse.
                            if (vRequest.TrySerialize(out ArrayBufferWriter<byte>? buffer, out int headerSize))
                            {
                                try
                                {
                                    //  Увеличить счетчик активных запросов.
                                    if (TryIncreaseActiveRequestsCount(isResponseRequired: !vRequest.IsNotification))
                                    {
                                        // Размер сообщения без заголовка.
                                        int messageSize = buffer.WrittenCount - headerSize;

                                        Debug.Assert(vRequest.Method != null);
                                        SetTcpNoDelay(vRequest.Method.TcpNoDelay);

                                        #region Отправка заголовка

                                        try
                                        {
                                            // Заголовок лежит в конце стрима.
                                            await SendBufferAsync(buffer.WrittenMemory.Slice(messageSize, headerSize), endOfMessage: true).ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        // Обрыв соединения.
                                        {
                                            var vException = new VRpcException(SR.SenderLoopError, ex);

                                            // Оповестить об обрыве.
                                            TryDispose(CloseReason.FromException(vException, _shutdownRequest));

                                            // Если запрос является нотификацией то нужно передать исключение ожидающему потоку.
                                            vRequest.CompleteSend(vException);

                                            // Завершить поток.
                                            return;
                                        }
                                        #endregion

                                        #region Отправка тела сообщения (запрос или ответ на запрос)

                                        if (messageSize > 0)
                                        {
                                            try
                                            {
                                                await SendBufferAsync(buffer.WrittenMemory.Slice(0, messageSize), true).ConfigureAwait(false);
                                            }
                                            catch (Exception ex)
                                            // Обрыв соединения.
                                            {
                                                var vException = new VRpcException(SR.SenderLoopError, ex);

                                                // Оповестить об обрыве.
                                                TryDispose(CloseReason.FromException(vException, _shutdownRequest));

                                                // Если запрос является нотификацией то нужно передать исключение ожидающему потоку.
                                                vRequest.CompleteSend(vException);

                                                // Завершить поток.
                                                return;
                                            }
                                        }
                                        #endregion

                                        // Если запрос является нотификацией то нужно завершить ожидание отправки.
                                        vRequest.CompleteSend();
                                    }
                                    else
                                    // Пользователь запросил остановку сервиса.
                                    {
                                        // Завершить поток.
                                        return;
                                    }
                                }
                                finally
                                {
                                    buffer.Return();
                                }
                            }
                        }
                    }
                    else if (message is IJRequest jRequest)
                    // Отправляем запрос или нотификацию.
                    {
                        if (jRequest.TryBeginSend())
                        {
                            // Если не удалось сериализовать -> Игнорируем отправку, пользователь получит исключение.
                            if (jRequest.TrySerialize(out ArrayBufferWriter<byte>? buffer))
                            {
                                try
                                {
                                    //  Увеличить счетчик активных запросов.
                                    if (TryIncreaseActiveRequestsCount(isResponseRequired: !jRequest.IsNotification))
                                    {
                                        Debug.Assert(jRequest.Method != null);
                                        SetTcpNoDelay(jRequest.Method.TcpNoDelay);

                                        DebugDisplayJson(buffer.WrittenMemory.Span);
                                        try
                                        {
                                            await SendBufferAsync(buffer.WrittenMemory, Ms.WebSocketMessageType.Text, endOfMessage: true).ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        // Обрыв соединения.
                                        {
                                            var vException = new VRpcException(SR.SenderLoopError, ex);

                                            // Оповестить об обрыве.
                                            TryDispose(CloseReason.FromException(vException, _shutdownRequest));

                                            // Если запрос является нотификацией то нужно передать исключение ожидающему потоку.
                                            jRequest.CompleteSend(vException);

                                            // Завершить поток.
                                            return;
                                        }

                                        // Если запрос является нотификацией то нужно завершить ожидание отправки.
                                        jRequest.CompleteSend();
                                    }
                                    else
                                        return;
                                }
                                finally
                                {
                                    buffer.Return();
                                }
                            }
                        }
                    }
                    else if (message is JErrorResponse jError)
                    {
                        ArrayBufferWriter<byte> buffer = jError.Serialize();
                        try
                        {
                            SetTcpNoDelay(tcpNoDelay: false);

                            DebugDisplayJson(buffer.WrittenMemory.Span);
                            try
                            {
                                await SendBufferAsync(buffer.WrittenMemory, Ms.WebSocketMessageType.Text, endOfMessage: true).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                TryDispose(CloseReason.FromException(new VRpcException(SR.SenderLoopError, ex), _shutdownRequest));

                                // Завершить поток.
                                return;
                            }
                        }
                        finally
                        {
                            buffer.Return();
                        }
                    }
                    else if (message is VErrorResponse vError)
                    {
                        ArrayBufferWriter<byte> buffer = vError.Serialize(out int headerSize);
                        try
                        {
                            // Размер сообщения без заголовка.
                            int messageSize = buffer.WrittenCount - headerSize;

                            SetTcpNoDelay(tcpNoDelay: false);

                            #region Отправка заголовка

                            try
                            {
                                // Заголовок лежит в конце стрима.
                                await SendBufferAsync(buffer.WrittenMemory.Slice(messageSize, headerSize), endOfMessage: true).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                TryDispose(CloseReason.FromException(new VRpcException(SR.SenderLoopError, ex), _shutdownRequest));

                                // Завершить поток.
                                return;
                            }
                            #endregion

                            #region Отправка тела сообщения (запрос или ответ на запрос)

                            if (messageSize > 0)
                            {
                                try
                                {
                                    await SendBufferAsync(buffer.WrittenMemory.Slice(0, messageSize), true).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве.
                                    TryDispose(CloseReason.FromException(new VRpcException(SR.SenderLoopError, ex), _shutdownRequest));

                                    // Завершить поток.
                                    return;
                                }
                            }
                            #endregion
                        }
                        finally
                        {
                            buffer.Return();
                        }
                    }
#if DEBUG
                    else
                    {
                        Debug.Assert(false, "Неизвестное сообщение");
                    }
#endif
                }
                else
                {
                    //if (message is ReusableJRequest reusable)
                    //    ReleaseReusable(reusable);
                }
            }
        }

        [Conditional("DEBUG")]
        private static void DebugDisplayJson(ReadOnlySpan<byte> span)
        {
#if DEBUG
            var debugDisplayAsString = new DebuggerDisplayJson(span);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetTcpNoDelay(bool tcpNoDelay)
        {
            if (_ws.Socket != null)
            {
                if (tcpNoDelay != _tcpNoDelay)
                {
                    _ws.Socket.NoDelay = _tcpNoDelay = tcpNoDelay;
                }
            }
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
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private protected abstract bool ActionPermissionCheck(ControllerMethodMeta actionMeta, 
            out IActionResult? permissionError, out ClaimsPrincipal? user);

        #region Монитор активных запросов.

        /// <summary>
        /// Увеличивает счётчик на 1 при получении запроса или при отправке запроса.
        /// </summary>
        /// <returns>False если был запрошен Shutdown и сервис нужно остановить.</returns>
        /// <remarks>Не бросает исключения.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryIncreaseActiveRequestsCount()
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

                // В отличии от Decrease, отправлять Close не нужно потому что это уже
                // сделал поток который уменьшил счётчик до 0.
                return false;
            }
        }

        /// <summary>
        /// Уменьшает счётчик после отправки ответа на запрос.
        /// </summary>
        /// <returns>False если сервис требуется остановить.</returns>
        /// <remarks>Не бросает исключения.</remarks>
        private bool DecreaseActiveRequestsCountOrClose()
        {
            if (TryDecreaseActiveRequestsCount())
            {
                return true;
            }
            else
            // Пользователь запросил остановку сервиса.
            {
                // В отличии от Increase тут мы обязаны отправить Close.
                TryBeginSendCloseBeforeShutdown();

                // Завершить поток.
                return false;
            }
        }

        /// <summary>
        /// Безусловно уменьшает счётчик активных запросов на 1.
        /// </summary>
        /// <returns>False если был запрошен Shutdown и сервис нужно остановить.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDecreaseActiveRequestsCount()
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

        #endregion

        #region Обработка запроса в отдельном потоке

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">Может быть Null если нотификация.</param>
        private void CreateRequestContextAndStart(int? id, ControllerMethodMeta method, object?[] args, bool isJsonRpc)
        {
            RequestContext? request = Interlocked.Exchange(ref _reusableContext, null);
            if (request != null)
            {
                request.Initialize(id, method, args, isJsonRpc);
            }
            else
            {
                Debug.WriteLine("Не удалось переиспользовать контекст запроса и был создан новый.");

                request = new RequestContext(this, id, method, args, isJsonRpc);
            }
            request.StartProcessRequest();
        }

        /// <summary>
        /// Выполняет запрос и отправляет результат или ошибку.
        /// </summary>
        /// <remarks>Точка входа потока из пула.</remarks>
        internal void OnStartProcessRequest(RequestContext requestContext)
        {
            // Сокет может быть уже закрыт, например по таймауту,
            // в этом случае ничего выполнять не нужно.
            if (!IsDisposed)
            {
                ProcessRequest(requestContext);
            }
            else
            {
                requestContext.DisposeArgs();
            }
        }

        private void ProcessRequest(RequestContext requestContext)
        {
            if (requestContext.IsResponseRequired)
            {
                Debug.Assert(requestContext.Id != null);

                ValueTask<object?> pendingRequestTask;

                // Этот блок Try должен быть идентичен тому который чуть ниже — для асинхронной обработки.
                try
                {
                    // Выполняет запрос и возвращает результат.
                    // Может быть исключение пользователя.
                    pendingRequestTask = InvokeControllerAsync(requestContext);
                }
                catch (VRpcInternalErrorException ex)
                {
                    // Вернуть результат с ошибкой.
                    SendInternalErrorResponse(requestContext, ex.Message);
                    return;
                }
                catch (Exception)
                // Злая ошибка обработки запроса -> Internal error (аналогично ошибке 500).
                {
                    // Вернуть результат с ошибкой.
                    SendInternalErrorResponse(requestContext);
                    return;
                }

                if (pendingRequestTask.IsCompletedSuccessfully)
                // Результат контроллера получен синхронно.
                {
                    // Не бросает исключения.
                    requestContext.Result = pendingRequestTask.Result;

                    SendOkResponse(requestContext);
                }
                else
                // Результат контроллера — асинхронный таск.
                {
                    WaitResponseAndSendAsync(pendingRequestTask, requestContext);

                    // TO THINK ошибки в таске можно обработать и не провоцируя исключения.
                    // ContinueWith должно быть в 5 раз быстрее. https://stackoverflow.com/questions/51923100/try-catchoperationcanceledexception-vs-continuewith
                    async void WaitResponseAndSendAsync(ValueTask<object?> task, RequestContext requestToInvoke)
                    {
                        Debug.Assert(requestToInvoke.Id != null);

                        // Этот блок Try должен быть идентичен тому который чуть выше — для синхронной обработки.
                        try
                        {
                            requestToInvoke.Result = await task.ConfigureAwait(false);
                        }
                        catch (VRpcInternalErrorException ex)
                        // Специальный тип исключения позволяющий быстро возвращать сообщение об ошибке.
                        {
                            // Вернуть результат с ошибкой.
                            SendInternalErrorResponse(requestToInvoke, ex.Message);
                            return;
                        }
                        catch (Exception)
                        // Злая ошибка обработки запроса. Аналогично ошибке 500.
                        {
                            // Вернуть результат с ошибкой.
                            SendInternalErrorResponse(requestToInvoke);
                            return;
                        }
                        SendOkResponse(requestToInvoke);
                    }
                }
            }
            else
            // Выполнить запрос без отправки ответа.
            {
                // Не бросает исключения.
                ProcessNotificationRequest(requestContext);
            }
        }

        /// <summary>
        /// Вызывает запрошенный метод контроллера и возвращает результат.
        /// Результатом может быть IActionResult или Raw объект или исключение.
        /// </summary>
        /// <exception cref="Exception">Исключение пользователя.</exception>
        /// <exception cref="ObjectDisposedException"/>
        /// <param name="requestContext">Гарантированно выполнит Dispose.</param>
        /// <returns><see cref="IActionResult"/> или любой объект.</returns>
        private ValueTask<object?> InvokeControllerAsync(RequestContext requestContext)
        {
            Debug.Assert(requestContext.Method != null);
            Debug.Assert(requestContext.Args != null);

            bool requestToDispose = true;
            IServiceScope? scopeToDispose = null;
            try
            {
                // Проверить доступ к функции.
                if (ActionPermissionCheck(requestContext.Method, out IActionResult? permissionError, out ClaimsPrincipal? user))
                {
                    IServiceScope scope = ServiceProvider.CreateScope();
                    scopeToDispose = scope;

                    // Инициализируем Scope текущим соединением.
                    var getProxyScope = scope.ServiceProvider.GetService<RequestContextScope>();
                    Debug.Assert(getProxyScope != null);
                    getProxyScope.Connection = this;

                    // Активируем контроллер через IoC.
                    var controller = scope.ServiceProvider.GetRequiredService(requestContext.Method.ControllerType) as RpcController;
                    Debug.Assert(controller != null);

                    // Подготавливаем контроллер.
                    controller.BeforeInvokeController(requestContext);
                    //controller.BeforeInvokeController(this, user);

                    //BeforeInvokeController(controller);

                    // Вызов метода контроллера.
                    // (!) Результатом может быть не завершённый Task.
                    object? actionResult = requestContext.Method.FastInvokeDelegate.Invoke(controller, requestContext.Args);

                    if (actionResult != null)
                    // Сконвертировать результат контроллера в Task<>.
                    {
                        // Может бросить исключение.
                        ValueTask<object?> actionResultAsTask = DynamicAwaiter.ConvertToTask(actionResult);

                        if (actionResultAsTask.IsCompletedSuccessfully)
                        {
                            // Извлекаем результат из Task'а.
                            actionResult = actionResultAsTask.Result;

                            // Результат успешно получен без исключения.
                            return new(actionResult);
                        }
                        else
                        // Будем ждать асинхронный результат.
                        {
                            // Предотвратить Dispose.
                            scopeToDispose = null;
                            requestToDispose = false;

                            return WaitForControllerActionAsync(actionResultAsTask, scope, requestContext);
                        }
                    }
                    else
                    // Результатом контроллера был Null.
                    {
                        return new(result: null);
                    }
                }
                else
                // Нет доступа к методу контроллера.
                {
                    return new(result: permissionError);
                }
            }
            finally
            {
                if (requestToDispose)
                    requestContext.DisposeArgs();

                // ServiceScope выполнит Dispose всем созданным экземплярам.
                scopeToDispose?.Dispose();
            }

            static async ValueTask<object?> WaitForControllerActionAsync(ValueTask<object?> task, IServiceScope scope, RequestContext pendingRequest)
            {
                try
                {
                    object? result = await task.ConfigureAwait(false);

                    // Результат успешно получен без исключения.
                    return result;
                }
                finally
                {
                    pendingRequest.DisposeArgs();
                    scope.Dispose();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void ProcessNotificationRequest(RequestContext notificationRequest)
        {
            Debug.Assert(notificationRequest.IsResponseRequired == false);

            ValueTask<object?> pendingRequestTask;
            try
            {
                // Может быть исключение пользователя.
                pendingRequestTask = InvokeControllerAsync(notificationRequest);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            // Злая ошибка обработки запроса.
            {
                DebugOnly.Break();
                NotificationError?.Invoke(this, ex);
                return;
            }

            if (!pendingRequestTask.IsCompletedSuccessfully)
            {
                WaitNotificationAsync(pendingRequestTask);
            }

            async void WaitNotificationAsync(ValueTask<object?> t)
            {
                try
                {
                    await t.ConfigureAwait(false);
                }
                catch (Exception ex)
                // Злая ошибка обработки запроса. Аналогично ошибке 500.
                {
                    DebugOnly.Break();
                    NotificationError?.Invoke(this, ex);
                }
            }
        }

        private void ReleaseReusable(RequestContext reusableRequest)
        {
            if (reusableRequest.IsReusable)
            {
                Debug.Assert(_reusableContext == null);

                reusableRequest.Reset();

                Volatile.Write(ref _reusableContext, reusableRequest);
            }
        }

        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private void SendOkResponse(RequestContext requestContext)
        {
            Debug.Assert(requestContext.Id != null);

            TryPostMessage(requestContext);
        }

        /// <summary>
        /// Отправляет ответ как "Internal error".
        /// </summary>
        private void SendInternalErrorResponse(RequestContext requestContext, string message = "Internal error")
        {
            Debug.Assert(requestContext.Id != null);

            requestContext.Result = new InternalErrorResult(message);

            // Вернуть результат с ошибкой.
            TryPostMessage(requestContext);
        }

        #endregion // Обработка запроса в отдельном потоке.

        /// <summary>
        /// Потокобезопасно освобождает ресурсы соединения. Вызывается при закрытии соединения.
        /// Взводит <see cref="Completion"/> и передаёт исключение всем ожидающим запросам.
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
                _pendingRequests.TryPropagateExceptionAndLockup(possibleReason.ToException());

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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //T IGetProxy.GetProxy<T>() where T : class => InnerGetProxy<T>();

        //private protected abstract T InnerGetProxy<T>() where T : class;

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// Полученный экземпляр можно привести к типу <see cref="ServerInterfaceProxy"/>.
        /// Метод является шорткатом для <see cref="GetProxyDecorator"/>
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public T GetProxy<T>() where T : class
        {
            T? proxy = GetProxyDecorator<T>().Proxy;
            Debug.Assert(proxy != null);
            return proxy;
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public ServerInterfaceProxy<T> GetProxyDecorator<T>() where T : class
        {
            return _proxyCache.GetProxyDecorator<T>(this);
        }

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

        [DebuggerHidden]
        void IThreadPoolWorkItem.Execute() => ReceiveLoop();
    }
}
