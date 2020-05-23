using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;
using DanilovSoft.WebSockets;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;
using Ms = System.Net.WebSockets;
using DanilovSoft.vRPC.Resources;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.IO.Pipelines;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Контекст соединения Web-Сокета. Владеет соединением.
    /// </summary>
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public abstract class ManagedConnection : IDisposable, IGetProxy
    {
        /// <summary>
        /// Содержит имена методов прокси интерфейса без постфикса Async.
        /// </summary>
        private protected abstract IConcurrentDictionary<MethodInfo, RequestMeta> InterfaceMethods { get; }
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly InvokeActionsDictionary _invokeActions;
        /// <summary>
        /// Для Completion.
        /// </summary>
        private readonly TaskCompletionSource<CloseReason> _completionTcs = new TaskCompletionSource<CloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
        [SuppressMessage("Microsoft.Usage", "CA2213", Justification = "Не требует вызывать Dispose если гарантированно будет вызван Cancel")]
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        /// <summary>
        /// Срабатывает когда соединение переходит в закрытое состояние.
        /// </summary>
        public CancellationToken CompletionToken => _cts.Token;
        /// <summary>
        /// Причина закрытия соединения. Это свойство возвращает <see cref="Completion"/>.
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
        private readonly Pipe _pipe;
        /// <summary>
        /// Коллекция запросов ожидающие ответ от удалённой стороны.
        /// </summary>
        private readonly RequestQueue _pendingRequests;
        public EndPoint LocalEndPoint { get; }
        public EndPoint RemoteEndPoint { get; }
        /// <summary>
        /// Отправка сообщения <see cref="BinaryMessageToSend"/> должна выполняться только через этот канал.
        /// </summary>
        private readonly Channel<BinaryMessageToSend> _sendChannel;
        private int _disposed;
        private bool IsDisposed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Volatile.Read(ref _disposed) == 1;
            }
        }
        /// <summary>
        /// <see langword="true"/> если происходит остановка сервиса.
        /// Используется для проверки возможности начать новый запрос.
        /// Использовать через блокировку <see cref="StopRequiredLock"/>.
        /// </summary>
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
        /// Является <see langword="volatile"/>. Если значение – <see langword="false"/>, то можно узнать причину через свойство <see cref="DisconnectReason"/>.
        /// Когда значение становится <see langword="false"/>, то вызывается событие <see cref="Disconnected"/>.
        /// После разъединения текущий экземпляр не может быть переподключен.
        /// </summary>
        public bool IsConnected => _isConnected;
        public abstract bool IsAuthenticated { get; }
        private Task? _loopSender;

        // static ctor.
        static ManagedConnection()
        {
            //var proto = ProtoBuf.Serializer.GetProto<HeaderDto>();
            //Console.WriteLine(proto);

            //ManagedWebSocket.DefaultNoDelay = true;
            //Debug.Assert(Marshal.SizeOf<HeaderDto>() <= 16, $"Структуру {nameof(HeaderDto)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<RequestMessageDto>() <= 16, $"Структуру {nameof(RequestMessageDto)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<RequestMessage>() <= 16, $"Структуру {nameof(RequestMessage)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<ValueWebSocketReceiveExResult>() <= 16, $"Структуру {nameof(ValueWebSocketReceiveExResult)} лучше заменить на класс");
            //Debug.Assert(Marshal.SizeOf<CloseReason>() <= 16, $"Структуру {nameof(CloseReason)} лучше заменить на класс");

            // Прогрев сериализатора.
            ProtoBuf.Serializer.PrepareSerializer<HeaderDto>();
            ExtensionMethods.WarmupRequestMessageJson();
        }

        // ctor.
        /// <summary>
        /// Принимает открытое соединение Web-Socket.
        /// </summary>
        internal ManagedConnection(ManagedWebSocket clientConnection, bool isServer, ServiceProvider serviceProvider, InvokeActionsDictionary actions)
        {
            IsServer = isServer;

            Debug.Assert(clientConnection.State == Ms.WebSocketState.Open);

            LocalEndPoint = clientConnection.LocalEndPoint;
            RemoteEndPoint = clientConnection.RemoteEndPoint;
            _ws = clientConnection;
            _pipe = new Pipe();

            _pendingRequests = new RequestQueue();

            // IoC готов к работе.
            ServiceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _invokeActions = actions;

            _sendChannel = Channel.CreateUnbounded<BinaryMessageToSend>(new UnboundedChannelOptions
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
            _loopSender = LoopSendAsync();

#if NETSTANDARD2_0 || NET472
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, this); // Без замыкания.
#else
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, this, preferLocal: true); // Без замыкания.
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
                closeReason = CloseReason.FromException(e.DisconnectingReason.Error, 
                    _shutdownRequest, e.DisconnectingReason.AdditionalDescription);
            }
            AtomicDispose(closeReason);
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
        /// Не бросает исключения.
        /// Потокобезопасно.
        /// </summary>
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

                    if (!DecActiveRequestCount())
                    // Нет ни одного ожадающего запроса.
                    {
                        // Можно безопасно остановить сокет.
                        // Не бросает исключения.
                        PrivateBeginClose(stopRequired.CloseDescription);
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
                var timeoutTask = Task.Delay(stopRequired.ShutdownTimeout);

                // Подождать грациозную остановку.
                if (await Task.WhenAny(_completionTcs.Task, timeoutTask).ConfigureAwait(false) == timeoutTask)
                {
                    Debug.WriteLine($"Достигнут таймаут {(int)stopRequired.ShutdownTimeout.TotalSeconds} сек. на грациозную остановку.");
                }

                // Не бросает исключения.
                AtomicDispose(CloseReason.FromException(new WasShutdownException(stopRequired)));

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
        /// Не бросает исключения.
        /// </summary>
        private void DisposeOnCloseReceived()
        {
            Debug.Assert(_shutdownRequest != null);

            // Был получен Close. Это значит что веб сокет уже закрыт и нам остаётся только закрыть сервис.
            AtomicDispose(CloseReason.FromCloseFrame(_ws.CloseStatus, _ws.CloseStatusDescription, null, _shutdownRequest));
        }

        /// <summary>
        /// Отправляет сообщение Close и ожидает ответный Close. Затем закрывает соединение.
        /// Не бросает исключения.
        /// </summary>
        private async void PrivateBeginClose(string? closeDescription)
        {
            // Эту функцию вызывает тот поток который поймал флаг о необходимости завершения сервиса.
            // Благодаря событию WebSocket.Disconnect у нас гарантированно вызовется AtomicDispose.

            // Нельзя делать Close одновременно с Send операцией.
            if (await FinishSenderAsync().ConfigureAwait(false))
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
                    AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                    // Завершить поток.
                    return;
                }
            }
        }

        /// <summary>
        /// Отправляет сообщение Close и ожидает ответный Close. Затем закрывает соединение.
        /// Не бросает исключения.
        /// </summary>
        private void BeginSendCloseBeforeShutdown()
        {
            Debug.Assert(_shutdownRequest != null);

            PrivateBeginClose(_shutdownRequest.CloseDescription);
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal object? OnInterfaceMethodCall(MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Создаём запрос для отправки.
            RequestMeta requestMeta = InterfaceMethods.GetOrAdd(targetMethod, (tm, cn) => new RequestMeta(tm, cn), controllerName);

            // Сериализуем запрос в память.
            BinaryMessageToSend serMsg = requestMeta.SerializeRequest(args);
            if (!requestMeta.IsNotificationRequest)
            {
                // Отправляем запрос.
                Task<object?> requestTask = SendRequestAndGetResult(requestMeta, serMsg);

                return ConvertRequestTask(requestMeta, requestTask);
            }
            else
            {
                PostNotification(serMsg, requestMeta);
                return ConvertNotificationTask(requestMeta, Task.CompletedTask);
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// Возвращает Task или готовый результат если был вызван синхронный метод.
        /// </summary>
        internal static object? OnClientInterfaceCall(ValueTask<ClientSideConnection> connectionTask, MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Создаём запрос для отправки.
            RequestMeta requestMeta = ClientSideConnection.InterfaceMethodsInfo.GetOrAdd(targetMethod, (mi, cn) => new RequestMeta(mi, cn), controllerName);

            // Сериализуем запрос в память. Лучше выполнить до подключения.
            BinaryMessageToSend serMsg = requestMeta.SerializeRequest(args);
            BinaryMessageToSend? toDispose = serMsg;

            try
            {
                // Результатом может быть не завершённый таск.
                object? activeCall = ExecuteRequestStatic(connectionTask, serMsg, requestMeta);
                toDispose = null; // Предотвратить Dispose.

                ValidateIfaceTypeMatch(targetMethod, activeCall);
                return activeCall;
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        [Conditional("DEBUG")]
        private static void ValidateIfaceTypeMatch(MethodInfo targetMethod, object? activeCall)
        {
            if (targetMethod.ReturnType != typeof(void) && activeCall != null)
            {
                Debug.Assert(targetMethod.ReturnType.IsInstanceOfType(activeCall), "Тип результата не совпадает с возвращаемым типом интерфейса");
            }
        }

        /// <summary>
        /// Отправляет запрос и возвращает результат. Результатом может быть Task, Task&lt;T&gt;, ValueTask, ValueTask&lt;T&gt; или готовый результат.
        /// </summary>
        private static object? ExecuteRequestStatic(ValueTask<ClientSideConnection> connectionTask, BinaryMessageToSend binaryRequest, RequestMeta requestMeta)
        {
            if (!requestMeta.IsNotificationRequest)
            // Запрос должен получить ответ.
            {
                return SendRequestAndGetResultStatic(connectionTask, binaryRequest, requestMeta);
            }
            else
            // Отправляем запрос как уведомление.
            {
                return SendNotificationStatic(connectionTask, binaryRequest, requestMeta);
            }
        }

        /// <summary>
        /// Отправляет запрос и возвращает результат. Результатом может быть Task, Task&lt;T&gt;, ValueTask, ValueTask&lt;T&gt; или готовый результат.
        /// </summary>
        private static object? SendRequestAndGetResultStatic(ValueTask<ClientSideConnection> connectionTask, BinaryMessageToSend binaryRequest, RequestMeta requestMeta)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            // Отправляем запрос.
            Task<object?> requestTask = SendRequestAndGetResultAsync(connectionTask, binaryRequest, requestMeta);

            // Результатом может быть не завершённый Task или готовый результат.
            return ConvertRequestTask(requestMeta, requestTask);
        }

        /// <summary>
        /// Отправляет запрос как уведомление. Результатом может быть Null или Task или ValueTask.
        /// </summary>
        private static object? SendNotificationStatic(ValueTask<ClientSideConnection> connectionTask, BinaryMessageToSend binaryRequest, RequestMeta requestMeta)
        {
            Debug.Assert(requestMeta.IsNotificationRequest);

            // Может бросить исключение.
            // Добавляет сообщение в очередь на отправку.
            Task sendNotificationTask = SendNotificationStaticAsync(connectionTask, binaryRequest, requestMeta);

            // Конвертируем Task в ValueTask если это требует интерфейс.
            return ConvertNotificationTask(requestMeta, sendNotificationTask);
        }

        /// <summary>
        /// Ожидает завершение подключения к серверу и передаёт сообщение в очередь на отправку.
        /// Может бросить исключение.
        /// Чаще всего возвращает <see cref="Task.CompletedTask"/>.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private static Task SendNotificationStaticAsync(ValueTask<ClientSideConnection> connectingTask, BinaryMessageToSend serializedMessage, RequestMeta requestMeta)
        {
            if (connectingTask.IsCompleted)
            {
                // Может бросить исключение.
                ManagedConnection connection = connectingTask.Result;
                
                // Отправляет уведомление через очередь.
                connection.PostNotification(serializedMessage, requestMeta);

                // Нотификации не возвращают результат.
                return Task.CompletedTask;
            }
            else
            // Подключение к серверу ещё не завершено.
            {
                return WaitForConnectAndSendNotification(connectingTask, serializedMessage, requestMeta);
            }

            // Локальная функция.
            static async Task WaitForConnectAndSendNotification(ValueTask<ClientSideConnection> t, BinaryMessageToSend serializedMessage, RequestMeta requestMeta)
            {
                ClientSideConnection connection = await t.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                connection.PostNotification(serializedMessage, requestMeta);
            }
        }

        private static Task<object?> SendRequestAndGetResultAsync(ValueTask<ClientSideConnection> connectionTask, BinaryMessageToSend serializedMessage, RequestMeta requestMetadata)
        {
            if (connectionTask.IsCompleted)
            {
                // Может быть исключение если не удалось подключиться.
                ClientSideConnection connection = connectionTask.Result;

                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendRequestAndGetResult(requestMetadata, serializedMessage);
            }
            else
            {
                return WaitForConnectAndSendRequest(connectionTask, serializedMessage, requestMetadata).Unwrap();
            }

            static async Task<Task<object?>> WaitForConnectAndSendRequest(ValueTask<ClientSideConnection> t, BinaryMessageToSend serializedMessage, RequestMeta requestMetadata)
            {
                ClientSideConnection connection = await t.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendRequestAndGetResult(requestMetadata, serializedMessage);
            }
        }

        /// <summary>
        /// Может вернуть Null или Task или ValueTask.
        /// </summary>
        private static object? ConvertNotificationTask(RequestMeta requestActionMeta, Task sendNotificationTask)
        {
            if (requestActionMeta.IsAsync)
            // Возвращаемый тип функции интерфейса — Task или ValueTask.
            {
                // Сконвертировать в ValueTask если такой тип у интерфейса.
                // Не бросает исключения.
                object convertedTask = EncapsulateValueTask(sendNotificationTask, requestActionMeta.ReturnType);

                // Результатом может быть не завершённый Task (или ValueTask).
                return convertedTask;
            }
            else
            // Была вызвана синхронная функция.
            {
                // Результатом может быть исключение.
                sendNotificationTask.GetAwaiter().GetResult();

                // У уведомлений нет результата.
                return null;
            }
        }

        /// <summary>
        /// Преобразует <see cref="Task"/><see langword="&lt;object&gt;"/> в <see cref="Task"/>&lt;T&gt; или возвращает TResult
        /// если метод интерфейса является синхронной функцией.
        /// </summary>
        private protected static object? ConvertRequestTask(RequestMeta requestMeta, Task<object?> requestTask)
        {
            if (requestMeta.IsAsync)
            // Возвращаемый тип функции интерфейса — Task.
            {
                if (requestMeta.ReturnType.IsGenericType)
                // У задачи есть результат.
                {
                    // Task<object> должен быть преобразован в Task<T>.
                    // Не бросает исключения.
                    return TaskConverter.ConvertTask(requestTask, requestMeta.IncapsulatedReturnType, requestMeta.ReturnType);
                }
                else
                {
                    if (requestMeta.ReturnType != typeof(ValueTask))
                    // Возвращаемый тип интерфейса – Task.
                    {
                        // Можно вернуть как Task<object>.
                        return requestTask;
                    }
                    else
                    // Возвращаемый тип интерфейса – ValueTask.
                    {
                        return new ValueTask(requestTask);
                    }
                }
            }
            else
            // Была вызвана синхронная функция.
            {
                // Результатом может быть исключение.
                object? finalResult = requestTask.GetAwaiter().GetResult();
                return finalResult;
            }
        }

        /// <summary>
        /// Возвращает Task или ValueTask для соответствия типу <paramref name="returnType"/>.
        /// Не бросает исключения.
        /// </summary>
        /// <param name="voidTask">Такс без результата (<see langword="void"/>).</param>
        private static object EncapsulateValueTask(Task voidTask, Type returnType)
        {
            if (returnType != typeof(ValueTask))
            // Возвращаемый тип интерфейса – Task.
            {
                // Конвертировать не нужно.
                return voidTask;
            }
            else
            // Возвращаемый тип интерфейса – ValueTask.
            {
                return new ValueTask(voidTask);
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу. 
        /// Добавляет запрос-уведомление в очередь на отправку (выполнит отправку текущим потоком если очередь пуста).
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal void PostNotification(BinaryMessageToSend serializedMessage, RequestMeta _)
        {
            ThrowIfDisposed();
            ThrowIfShutdownRequired();
            //ValidateAuthenticationRequired(requestMeta);

            // Планируем отправку запроса.
            QueueSendMessage(serializedMessage);
        }

        //private protected Task<object> SendRequestAndGetResult(BinaryMessageToSend serializedMessage, RequestMeta requestMeta)
        //{
        //    return SendRequestAndGetResultTask(serializedMessage, requestMeta);
        //}

        /// <summary>
        /// Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private protected Task<object?> SendRequestAndGetResult(RequestMeta requestMeta, object[] args)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            BinaryMessageToSend? request = requestMeta.SerializeRequest(args);
            try
            {
                Task<object?> requestTask = SendRequestAndGetResult(requestMeta, request);
                request = null;
                return requestTask;
            }
            finally
            {
                request?.Dispose();
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу. Отправляет запрос и ожидает его ответ.
        /// Если не случилось исключения то <paramref name="serializedMessage"/> диспозить нельзя.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private protected Task<object?> SendRequestAndGetResult(RequestMeta requestMeta, BinaryMessageToSend serializedMessage)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);
            BinaryMessageToSend? toDispose = serializedMessage;

            try
            {
                ThrowIfDisposed();
                ThrowIfShutdownRequired();

                // Добавить запрос в словарь для дальнейшей связки с ответом.
                RequestAwaiter tcs = _pendingRequests.AddRequest(requestMeta, out int uid);

                // Назначить запросу уникальный идентификатор.
                serializedMessage.Uid = uid;

                // Планируем отправку запроса.
                // Не бросает исключения.
                QueueSendMessage(serializedMessage);

                // Предотвратить Dispose на месте.
                toDispose = null;

                // Ожидаем результат от потока поторый читает из сокета.
                return WaitForAwaiterAsync(tcs);
            }
            finally
            {
                toDispose?.Dispose();
            }

            static async Task<object?> WaitForAwaiterAsync(RequestAwaiter tcs)
            {
                // Ожидаем результат от потока поторый читает из сокета.
                // Валидным результатом может быть исключение.
                object? rawResult = await tcs;

                // Успешно получили результат без исключений.
                return rawResult;
            }
        }

        private async void ReceiveLoop()
        {
            byte[] headerBuffer = new byte[HeaderDto.HeaderMaxSize];

            // Бесконечно обрабатываем сообщения сокета.
            while (!IsDisposed)
            {
                #region Читаем хедер

                //if (!await ReceiveHeaderAsync(headerBuffer).ConfigureAwait(false))
                //{
                //    // Завершить поток.
                //    return;
                //}

                ValueWebSocketReceiveResult webSocketMessage;

                int bufferOffset = 0;
                do
                {
                    try
                    {
                        // Читаем фрейм веб-сокета.
                        webSocketMessage = await _ws.ReceiveExAsync(headerBuffer.AsMemory(bufferOffset), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    // Обрыв соединения.
                    {
                        // Оповестить об обрыве.
                        AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                        // Завершить поток.
                        return;
                    }

                    bufferOffset += webSocketMessage.Count;

                } while (!webSocketMessage.EndOfMessage);

                HeaderDto header;
                if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Binary)
                {
                    try
                    {
                        header = HeaderDto.DeserializeProtobuf(headerBuffer, 0, webSocketMessage.Count);
                    }
                    catch (Exception headerException)
                    // Не удалось десериализовать заголовок.
                    {
                        Debug.WriteLine(headerException);

                        // Отправка Close и выход
                        var propagateException = new RpcProtocolErrorException(SR2.GetString(SR.ProtocolError, headerException.Message), headerException);
                        await CloseAndDisposeAsync(propagateException, $"Unable to deserialize header. Count of bytes was {webSocketMessage.Count}").ConfigureAwait(false);
                        return;
                    }
                }
                else if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Text)
                // Тип Text не поддерживается.
                {
                    // Тип фрейма должен быть Binary.
                    var protocolErrorException = new RpcProtocolErrorException(SR.TextMessageTypeNotSupported);

                    // Отправка Close и выход
                    await CloseAndDisposeAsync(protocolErrorException, SR.TextMessageTypeNotSupported).ConfigureAwait(false);
                    return;
                }
                else
                // Получен Close.
                {
                    if(_ws.State == Ms.WebSocketState.CloseReceived)
                    {
                        if (await FinishSenderAsync().ConfigureAwait(false))
                        {
                            try
                            {
                                await _ws.CloseOutputAsync(_ws.CloseStatus!.Value, _ws.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                                // Завершить поток.
                                return;
                            }
                        }
                    }

                    DisposeOnCloseReceived();

                    // Завершить поток.
                    return;
                }

#endregion

                if (header != null)
                {
                    //Debug.WriteLine($"Received Header: {header}");

                    using (var contentMemHandler = MemoryPool<byte>.Shared.Rent(header.ContentLength))
                    {
                        Memory<byte> contentMem = null;

                        if (header.ContentLength > 0)
                        // Есть дополнительный фрейм с телом сообщения.
                        {
                            // Можно не очищать – буффер будет перезаписан.
                            contentMem = contentMemHandler.Memory.Slice(0, header.ContentLength);

                            bufferOffset = 0;

                            // Сколько байт должны принять в следующих фреймах.
                            int receiveMessageBytesLeft = header.ContentLength;

                            do // Читаем и склеиваем фреймы веб-сокета пока не EndOfMessage.
                            {
                                // Проверить на ошибку протокола.
                                if(receiveMessageBytesLeft == 0)
                                // Считали сколько заявлено в ContentLength но сообщение оказалось больше.
                                {
                                    // Размер данных оказался больше чем заявлено в ContentLength.
                                    var protocolErrorException = new RpcProtocolErrorException("Размер сообщения оказался больше чем заявлено в заголовке");

                                    // Отправка Close и выход
                                    await CloseAndDisposeAsync(
                                        protocolErrorException, 
                                        closeDescription: "Web-Socket message size was larger than specified in the header's 'ContentLength'").ConfigureAwait(false);

                                    // Завершить поток.
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
                                    // Оповестить об обрыве.
                                    AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                                    // Завершить поток.
                                    return;
                                }
#endregion

                                if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Binary)
                                {
                                    bufferOffset += webSocketMessage.Count;
                                    receiveMessageBytesLeft -= webSocketMessage.Count;
                                }
                                else if(webSocketMessage.MessageType == Ms.WebSocketMessageType.Text)
                                {
                                    // Тип фрейма должен быть Binary.
                                    var protocolErrorException = new RpcProtocolErrorException(SR.TextMessageTypeNotSupported);

                                    // Отправка Close и выход
                                    await CloseAndDisposeAsync(protocolErrorException, SR.TextMessageTypeNotSupported).ConfigureAwait(false);
                                    return;
                                }
                                else
                                // Другая сторона закрыла соединение.
                                {
                                    if (_ws.State == Ms.WebSocketState.CloseReceived)
                                    {
                                        if (await FinishSenderAsync().ConfigureAwait(false))
                                        {
                                            try
                                            {
                                                await _ws.CloseOutputAsync(_ws.CloseStatus!.Value, _ws.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                                            }
                                            catch (Exception ex)
                                            // Обрыв соединения.
                                            {
                                                // Оповестить об обрыве.
                                                AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                                                // Завершить поток.
                                                return;
                                            }
                                        }
                                    }

                                    DisposeOnCloseReceived();

                                    // Завершить поток.
                                    return;
                                }
#endregion

                            } while (!webSocketMessage.EndOfMessage);
                        }

#region Обработка Payload

                        if (header.StatusCode == StatusCode.Request)
                        // Получен запрос.
                        {
                            if (header.Uid != null)
                            {
                                if (!IncActiveRequestCount())
                                // Происходит остановка. Выполнять запрос не нужно.
                                {
                                    return;
                                }
                            }

#region Десериализация запроса

                            RequestToInvoke? requestToInvoke;
                            IActionResult? error = null;
                            try
                            {
                                // Десериализуем запрос.
                                requestToInvoke = JsonRequestParser.TryDeserializeRequestJson(contentMem.Span, _invokeActions, header, out error);
                            }
                            catch (Exception ex)
                            // Ошибка десериализации запроса.
                            {
#region Игнорируем запрос

                                if (header.Uid != null)
                                // Запрос ожидает ответ.
                                {
                                    // Подготовить ответ с ошибкой.
                                    var errorResponse = new ResponseMessage(header.Uid.Value, new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{ex.Message}\"."));

                                    // Передать на отправку результат с ошибкой через очередь.
                                    QueueSendResponse(errorResponse);
                                }

                                // Вернуться к чтению заголовка.
                                continue;
#endregion
                            }
#endregion

#region Не удалось десериализовать — игнорируем запрос

                            if (requestToInvoke == null)
                            // Не удалось десериализовать запрос.                                    
                            {
                                if (header.Uid != null)
                                // Запрос ожидает ответ.
                                {
                                    //Debug.Assert(header.Uid != null, "header.Uid оказался Null");
                                    Debug.Assert(error != null);

                                    // Передать на отправку результат с ошибкой через очередь.
                                    QueueSendResponse(new ResponseMessage(header.Uid.Value, error));
                                }

                                // Вернуться к чтению заголовка.
                                continue;
                            }
#endregion

                            // Начать выполнение запроса в отдельном потоке.
                            StartProcessRequest(requestToInvoke);
                        }
                        else
                        // Получен ответ на запрос.
                        {
#region Передача другому потоку ответа на запрос

                            // Удалить запрос из словаря.
                            if (_pendingRequests.TryRemove(header.Uid.Value, out RequestAwaiter reqAwaiter))
                            // Передать ответ ожидающему потоку.
                            {
#region Передать ответ ожидающему потоку

                                if (header.StatusCode == StatusCode.Ok)
                                // Запрос на удалённой стороне был выполнен успешно.
                                {
#region Передать успешный результат

                                    if (reqAwaiter.Request.IncapsulatedReturnType != typeof(void))
                                    // Поток ожидает некий объект как результат.
                                    {
                                        bool deserialized;
                                        object? rawResult;
                                        if (!contentMem.IsEmpty)
                                        {
                                            // Десериализатор в соответствии с ContentEncoding.
                                            Func<ReadOnlyMemory<byte>, Type, object> deserializer = header.GetDeserializer();
                                            try
                                            {
                                                rawResult = deserializer(contentMem, reqAwaiter.Request.IncapsulatedReturnType);
                                                deserialized = true;
                                            }
                                            catch (Exception deserializationException)
                                            {
                                                deserialized = false;
                                                rawResult = null;

                                                var protocolErrorException = new RpcProtocolErrorException(
                                                    $"Ошибка десериализации ответа на запрос \"{reqAwaiter.Request.ActionFullName}\".", deserializationException);

                                                // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удаленной стороны.
                                                reqAwaiter.TrySetException(protocolErrorException);
                                            }
                                        }
                                        else
                                        // У ответа отсутствует контент — это равнозначно Null.
                                        {
                                            if (reqAwaiter.Request.IncapsulatedReturnType.CanBeNull())
                                            // Результат запроса поддерживает Null.
                                            {
                                                deserialized = true;
                                                rawResult = null;
                                            }
                                            else
                                            // Результатом этого запроса не может быть Null.
                                            {
                                                deserialized = false;
                                                rawResult = null;

                                                var protocolErrorException = new RpcProtocolErrorException(
                                                    $"Ожидался не пустой результат запроса но был получен ответ без результата.");

                                                // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удаленной стороны.
                                                reqAwaiter.TrySetException(protocolErrorException);
                                            }
                                        }

                                        if (deserialized)
                                        {
                                            // Передать результат ожидающему потоку.
                                            reqAwaiter.TrySetResult(rawResult);
                                        }
                                    }
                                    else
                                    // void.
                                    {
                                        reqAwaiter.TrySetResult(null);
                                    }
#endregion
                                }
                                else
                                // Сервер прислал код ошибки.
                                {
                                    // Телом ответа в этом случае будет строка.
                                    string errorMessage = contentMem.ReadAsString();

                                    // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                                    reqAwaiter.TrySetException(new BadRequestException(errorMessage, header.StatusCode));
                                }
#endregion

                                // Получен ожидаемый ответ на запрос.
                                if (DecActiveRequestCount())
                                {
                                    continue;
                                }
                                else
                                // Пользователь запросил остановку сервиса.
                                {
                                    // Не бросает исключения.
                                    BeginSendCloseBeforeShutdown();

                                    // Завершить поток.
                                    return;
                                }
                            }

#endregion
                        }
#endregion
                    }
                }
                else
                // Ошибка в хедере.
                {
                    // Отправка Close и выход.
                    var protocolException = new RpcProtocolErrorException("Не удалось десериализовать полученный заголовок сообщения.");
                    await CloseAndDisposeAsync(protocolException, $"Unable to deserialize header. Count of bytes was {webSocketMessage.Count}").ConfigureAwait(false);
                    return;
                }
            }
        }

        //private async ValueTask<bool> ReceiveHeaderAsync(byte[] headerBuffer)
        //{
        //    ValueWebSocketReceiveResult webSocketMessage;

        //    int bufferOffset = 0;
        //    do
        //    {
        //        try
        //        {
        //            // Читаем фрейм веб-сокета.
        //            webSocketMessage = await _ws.ReceiveExAsync(headerBuffer.AsMemory(bufferOffset), CancellationToken.None).ConfigureAwait(false);
        //        }
        //        catch (Exception ex)
        //        // Обрыв соединения.
        //        {
        //            // Оповестить об обрыве.
        //            AtomicDispose(CloseReason.FromException(ex, _stopRequired));

        //            // Завершить поток.
        //            return false;
        //        }

        //        bufferOffset += webSocketMessage.Count;

        //    } while (!webSocketMessage.EndOfMessage);

        //    return true;
        //}

        /// <summary>
        /// Гарантирует что ничего больше не будет отправлено через веб-сокет. 
        /// Дожидается завершения отправляющего потока.
        /// Не бросает исключения.
        /// Атомарно возвращает true означающее что поток должен выполнить Close.
        /// </summary>
        private async Task<bool> FinishSenderAsync()
        {
            bool completed = _sendChannel.Writer.TryComplete();
            Task? senderTask = Volatile.Read(ref _loopSender);
            if (senderTask != null)
            // Подождать завершение Send потока.
            {
                await senderTask.ConfigureAwait(false);
            }
            return completed;
        }

        /// <summary>
        /// Отправляет Close и выполняет Dispose.
        /// </summary>
        /// <param name="protocolErrorException">Распространяет исключение ожидаюшим потокам.</param>
        private async Task CloseAndDisposeAsync(Exception protocolErrorException, string closeDescription)
        {
            // Сообщить потокам что обрыв произошел по вине удалённой стороны.
            _pendingRequests.PropagateExceptionAndLockup(protocolErrorException);

            if (await FinishSenderAsync().ConfigureAwait(false))
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
                    AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                    // Завершить поток.
                    return;
                }
            }

            // Оповестить об обрыве.
            AtomicDispose(CloseReason.FromException(protocolErrorException, _shutdownRequest));

            // Завершить поток.
            return;
        }

        /// <summary>
        /// Сериализует сообщение в новом потоке и добавляет в очередь на отправку.
        /// Уменьшит <see cref="_activeRequestCount"/>.
        /// Не должно бросать исключения(!).
        /// </summary>
        /// <param name="responseToSend"></param>
        private void QueueSendResponse(ResponseMessage responseToSend)
        {
#if NETSTANDARD2_0 || NET472
            ThreadPool.UnsafeQueueUserWorkItem(QueueSendResponseThread, (this, responseToSend));
#else
            ThreadPool.UnsafeQueueUserWorkItem(QueueSendResponseThread, (this, responseToSend), preferLocal: true);
#endif
        }

#if NETSTANDARD2_0 || NET472

        private static void QueueSendResponseThread(object? state)
        {
            Debug.Assert(state != null);
            var tuple = ((ManagedConnection, ResponseMessage))state!;

            QueueSendResponseThread(argState: tuple);
        }
#endif

        private static void QueueSendResponseThread((ManagedConnection self, ResponseMessage responseToSend) argState)
        {
            // Сериализуем.
            BinaryMessageToSend serializedMessage = SerializeResponse(argState.responseToSend);

            // Ставим в очередь.
            argState.self.QueueSendMessage(serializedMessage);
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        private static BinaryMessageToSend SerializeResponse(ResponseMessage responseToSend)
        {
            BinaryMessageToSend refCopy;
            BinaryMessageToSend? serMsg = new BinaryMessageToSend(responseToSend);
            try
            {
                if (responseToSend.ActionResult is IActionResult actionResult)
                {
                    var actionContext = new ActionContext(responseToSend.ReceivedRequest, serMsg.MemPoolStream);
                    
                    // Сериализуем ответ.
                    actionResult.ExecuteResult(actionContext);
                    serMsg.StatusCode = actionContext.StatusCode;
                    serMsg.ContentEncoding = actionContext.ProducesEncoding;
                }
                else
                {
                    // Сериализуем ответ.
                    serMsg.StatusCode = StatusCode.Ok;

                    object? result = responseToSend.ActionResult;
                    if (result != null)
                    {
                        responseToSend.ReceivedRequest.ActionToInvoke.Serializer(serMsg.MemPoolStream, result);
                        serMsg.ContentEncoding = responseToSend.ReceivedRequest.ActionToInvoke.ProducesEncoding;
                    }
                }

                refCopy = serMsg;
                serMsg = null; // Предотвратить Dispose.
            }
            finally
            {
                if(serMsg != null)
                    serMsg.Dispose();
            }
            return refCopy;
        }

        /// <summary>
        /// Сериализует хэдер в стрим сообщения. Не бросает исключения.
        /// </summary>
        private static void AppendHeader(BinaryMessageToSend messageToSend)
        {
            HeaderDto header = CreateHeader(messageToSend);

            // Записать заголовок в конец стрима. Не бросает исключения.
            header.SerializeProtoBuf(messageToSend.MemPoolStream, out int headerSize);

            // Запомним размер хэдера.
            messageToSend.HeaderSize = headerSize;
        }

        private static HeaderDto CreateHeader(BinaryMessageToSend messageToSend)
        {
            Debug.Assert(messageToSend != null);

            if (messageToSend.MessageToSend is ResponseMessage responseToSend)
            // Создать хедер ответа на запрос.
            {
                Debug.Assert(messageToSend.StatusCode != null, "StatusCode ответа не может быть Null");

                return HeaderDto.FromResponse(responseToSend.Uid, messageToSend.StatusCode.Value, (int)messageToSend.MemPoolStream.Length, messageToSend.ContentEncoding);
            }
            else
            // Создать хедер для нового запроса.
            {
                return HeaderDto.CreateRequest(messageToSend.Uid, (int)messageToSend.MemPoolStream.Length);
            }
        }

        /// <summary>
        /// Добавляет хэдер и передает на отправку другому потоку.
        /// Не бросает исключения.
        /// </summary>
        private void QueueSendMessage(BinaryMessageToSend messageToSend)
        {
            Debug.Assert(messageToSend != null);
            BinaryMessageToSend? toDispose = messageToSend;

            try
            {
                // На текущем этапе сокет может быть уже уничтожен другим потоком.
                // В этом случае можем беспоследственно проигнорировать отправку; вызывающий получит исключение через RequestAwaiter.
                if (!IsDisposed)
                {
                    // Сериализуем хедер. Не бросает исключения.
                    AppendHeader(messageToSend);

                    // Передать на отправку.
                    // (!) Из-за AllowSynchronousContinuations частично начнёт отправку текущим потоком.
                    if (_sendChannel.Writer.TryWrite(messageToSend))
                    // Канал ещё не закрыт (не был вызван Dispose).
                    {
                        // Предотвратить Dispose.
                        toDispose = null;
                        return;
                    }
                }
            }
            finally
            {
                toDispose?.Dispose();
            }
        }

        /// <summary>
        /// Принимает заказы на отправку и отправляет в сокет. Запускается из конструктора. Не бросает исключения.
        /// </summary>
        /// <returns></returns>
        private async Task LoopSendAsync() // Точка входа нового потока.
        {
            while (!IsDisposed)
            {
                // Ждём сообщение для отправки.
                if (await _sendChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    // Всегда true — у нас только один читатель.
                    _sendChannel.Reader.TryRead(out BinaryMessageToSend serializedMessage);

                    Debug.Assert(serializedMessage != null);

                    // Теперь мы владеем этим объектом.
                    using (serializedMessage)
                    {
                        if (serializedMessage.MessageToSend.IsRequest)
                        // Происходит отправка запроса, а не ответа на запрос.
                        {
                            if (!serializedMessage.MessageToSend.IsNotificationRequest)
                            // Должны получить ответ на этот запрос.
                            {
                                if (!IncActiveRequestCount())
                                // Происходит остановка и сокет уже уничтожен.
                                {
                                    // Просто завершить поток.
                                    return;
                                }
                            }
                        }

                        LogSend(serializedMessage);

                        byte[] streamBuffer = serializedMessage.MemPoolStream.GetBuffer();

                        // Размер сообщения без заголовка.
                        int messageSize = (int)serializedMessage.MemPoolStream.Length - serializedMessage.HeaderSize;

#region Отправка заголовка

                        try
                        {
                            // Заголовок лежит в конце стрима.
                            await _ws.SendAsync(streamBuffer.AsMemory(messageSize, serializedMessage.HeaderSize),
                                Ms.WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                            // Завершить поток.
                            return;
                        }
#endregion

                        if (messageSize > 0)
                        {
#region Отправка тела сообщения (запрос или ответ на запрос)

                            try
                            {
                                await _ws.SendAsync(streamBuffer.AsMemory(0, messageSize),
                                    Ms.WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDispose(CloseReason.FromException(ex, _shutdownRequest));

                                // Завершить поток.
                                return;
                            }

#endregion
                        }

                        if (!serializedMessage.MessageToSend.IsRequest)
                        // Ответ успешно отправлен.
                        {
                            if (DecActiveRequestCount())
                            {
                                continue;
                            }
                            else
                            // Пользователь запросил остановку сервиса.
                            {
                                // Не бросает исключения.
                                BeginSendCloseBeforeShutdown();

                                // Завершить поток.
                                return;
                            }
                        }
                    }
                }
                else
                // Другой поток закрыл канал.
                {
                    // Завершить поток.
                    return;
                }
            }
        }

        [Conditional("LOG_RPC")]
        private static void LogSend(BinaryMessageToSend serializedMessage)
        {
            byte[] streamBuffer = serializedMessage.MemPoolStream.GetBuffer();

            // Размер сообщения без заголовка.
            int contentSize = (int)serializedMessage.MemPoolStream.Length - serializedMessage.HeaderSize;

            var headerSpan = streamBuffer.AsSpan(contentSize, serializedMessage.HeaderSize);
            //var contentSpan = streamBuffer.AsSpan(0, contentSize);

            var header = HeaderDto.DeserializeProtobuf(headerSpan.ToArray(), 0, headerSpan.Length);
            //string header = HeaderDto.DeserializeProtobuf(headerSpan.ToArray(), 0, headerSpan.Length).ToString();

            //string header = Encoding.UTF8.GetString(headerSpan.ToArray());
            //string content = Encoding.UTF8.GetString(contentSpan.ToArray());

            //header = Newtonsoft.Json.Linq.JToken.Parse(header).ToString(Newtonsoft.Json.Formatting.Indented);
            Debug.WriteLine(header);

            if ((header.StatusCode == StatusCode.Ok || header.StatusCode == StatusCode.Request) 
                && (serializedMessage.ContentEncoding == null || serializedMessage.ContentEncoding == "json"))
            {
                if (contentSize > 0)
                {
                    try
                    {
                        string content = System.Text.Json.JsonDocument.Parse(streamBuffer.AsMemory(0, contentSize)).RootElement.ToString();
                        Debug.WriteLine(content);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                        if (Debugger.IsAttached)
                            Debugger.Break();
                    }
                }
            }
            else
            {
                //Debug.WriteLine(streamBuffer.AsMemory(0, contentSize).Span.Select(x => x).ToString());
            }
        }

        /// <summary>
        /// Вызывает запрошенный метод контроллера и возвращает результат.
        /// Результатом может быть IActionResult или Raw объект или исключение.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        private ValueTask<object?> InvokeControllerAsync(RequestToInvoke receivedRequest)
        {
            // Проверить доступ к функции.
            if (ActionPermissionCheck(receivedRequest.ActionToInvoke, out IActionResult? permissionError, out ClaimsPrincipal? user))
            {
                IServiceScope scope = ServiceProvider.CreateScope();
                IServiceScope? toDispose = scope;
                try
                {
                    // Инициализируем Scope текущим соединением.
                    var getProxyScope = scope.ServiceProvider.GetService<GetProxyScope>();
                    getProxyScope.GetProxy = this;

                    // Активируем контроллер через IoC.
                    var controller = scope.ServiceProvider.GetRequiredService(receivedRequest.ActionToInvoke.ControllerType) as Controller;
                    Debug.Assert(controller != null);

                    // Подготавливаем контроллер.
                    controller.BeforeInvokeController(this, user);

                    //BeforeInvokeController(controller);

                    // Вызов метода контроллера.
                    object? actionResult = receivedRequest.ActionToInvoke.FastInvokeDelegate(controller, receivedRequest.Args);

                    // Может быть не завершённый Task.
                    if (actionResult != null)
                    {
                        ValueTask<object?> t = DynamicAwaiter.WaitAsync(actionResult);

                        if (t.IsCompletedSuccessfully)
                        {
                            // Извлекает результат из Task'а.
                            actionResult = t.Result;

                            // Результат успешно получен без исключения.
                            return new ValueTask<object?>(actionResult);
                        }
                        else
                        {
                            // Предотвратить Dispose.
                            toDispose = null;

                            return WaitForControllerActionAsync(t, scope);
                        }
                    }
                    else
                    {
                        return new ValueTask<object?>(result: null);
                    }
                }
                finally
                {
                    // ServiceScope выполнит Dispose всем созданным экземплярам.
                    toDispose?.Dispose();
                }
            }
            else
            {
                return new ValueTask<object?>(permissionError);
            }

            static async ValueTask<object?> WaitForControllerActionAsync(ValueTask<object?> t, IServiceScope scope)
            {
                using (scope)
                {
                    object? result = await t.ConfigureAwait(false);

                    // Результат успешно получен без исключения.
                    return result;
                }
            }
        }

        //private protected abstract void PrepareController();

        /// <summary>
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// Не бросает исключения.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        private protected abstract bool ActionPermissionCheck(ControllerActionMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user);

        /// <summary>
        /// В новом потоке выполняет запрос и отправляет ему результат или ошибку.
        /// </summary>
        private void StartProcessRequest(RequestToInvoke request)
        {
#if NETSTANDARD2_0 || NET472
            ThreadPool.UnsafeQueueUserWorkItem(StartProcessRequestThread, (this, request)); // Без замыкания.
#else
            ThreadPool.UnsafeQueueUserWorkItem(StartProcessRequestThread, (this, request), preferLocal: true); // Без замыкания.
#endif
        }

#if NETSTANDARD2_0 || NET472
        private static void StartProcessRequestThread(object? state)
        {
            Debug.Assert(state != null);
            var tuple = ((ManagedConnection, RequestToInvoke))state;

            StartProcessRequestThread(stateTuple: tuple);
        }  
#endif

        private static void StartProcessRequestThread((ManagedConnection self, RequestToInvoke request) stateTuple)
        {
            // Не бросает исключения.
            stateTuple.self.StartProcessRequestThread(stateTuple.request);
        }

        /// <summary>
        /// Выполняет запрос и отправляет результат или ошибку.
        /// </summary>
        /// <param name="requestContext"></param>
        private void StartProcessRequestThread(RequestToInvoke requestContext) // Точка входа потока из пула.
        {
            // В редких случаях сокет может быть уже закрыт, например по таймауту,
            // в этом случае ничего выполнять не нужно.
            if (!IsDisposed)
            {
                ProcessRequest(requestContext);
            }
        }

        /// <summary>
        /// Увеличивает счётчик на 1 при получении запроса или при отправке запроса.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IncActiveRequestCount()
        {
            //LogInc();

            // Увеличить счетчик запросов.
            if (Interlocked.Increment(ref _activeRequestCount) > 0)
            {
                return true;
            }
            else
            // Значение было -1, значит происходит остановка. Выполнять запрос не нужно.
            {
                return false;
            }
        }

        /// <summary>
        /// Уменьшает счётчик на 1 при получении ответа на запрос или при отправке ответа на запрос.
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool DecActiveRequestCount()
        {
            //LogDec();

            // Получен ожидаемый ответ на запрос.
            if (Interlocked.Decrement(ref _activeRequestCount) != -1)
            {
                return true;
            }
            else
            // Пользователь запросил остановку сервиса.
            {
                return false;
            }
        }

        private void ProcessRequest(RequestToInvoke requestToInvoke)
        {
            if (requestToInvoke.Uid != null)
            {
                // Выполняет запрос в текущем процессе и возвращает ответ.
                ValueTask<ResponseMessage> task = GetResponseAsync(requestToInvoke);

                if (task.IsCompleted)
                {
                    // Не бросает исключения.
                    ResponseMessage responseMessage = task.Result;

                    // Не бросает исключения.
                    SerializeAndSendResponse(responseMessage);
                }
                else
                {
                    WaitResponseAndSendAsync(task);
                }
            }
            else
            // Notification
            {
                ValueTask<object?> t = InvokeControllerAsync(requestToInvoke);

                if (!t.IsCompletedSuccessfully)
                {
                    WaitForNotification(t);
                }
            }
        }

        private static async void WaitForNotification(ValueTask<object?> t)
        {
            try
            {
                await t.ConfigureAwait(false);
            }
            catch (Exception ex)
            // Злая ошибка обработки запроса. Аналогично ошибке 500.
            {
                // Прервать отладку.
                DebugOnly.Break();

                Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// Не бросает исключения.
        /// </summary>
        private void SerializeAndSendResponse(ResponseMessage responseMessage)
        {
            // Не бросает исключения.
            BinaryMessageToSend responseToSend = SerializeResponse(responseMessage);

            // Не бросает исключения.
            QueueSendMessage(responseToSend);
        }

        private async void WaitResponseAndSendAsync(ValueTask<ResponseMessage> task)
        {
            // Не бросает исключения.
            // Выполняет запрос и возвращает ответ.
            ResponseMessage responseMessage = await task.ConfigureAwait(false);

            // Не бросает исключения.
            SerializeAndSendResponse(responseMessage);
        }

        /// <summary>
        /// Выполняет запрос клиента и инкапсулирует результат в <see cref="ResponseMessage"/>.
        /// Не бросает исключения.
        /// </summary>
        private ValueTask<ResponseMessage> GetResponseAsync(RequestToInvoke requestContext)
        {
            // Не должно бросать исключения.
            ValueTask<object?> t = InvokeControllerAsync(requestContext);

            if (t.IsCompletedSuccessfully)
            // Синхронно только в случае успеха.
            {
                // Результат контроллера. Может быть Task.
                object? result = t.Result;

                return new ValueTask<ResponseMessage>(new ResponseMessage(requestContext, result));
            }
            else
            {
                return WaitForInvokeControllerAsync(t, requestContext);
            }
        }

        //private static BinaryMessageToSend SerializeResponse(ResponseMessage response, RequestToInvoke requestContext)
        //{
        //    if (response != null)
        //    {
        //        // Сериализуется без исключения.
        //        return SerializeResponse(response);
        //    }
        //    else
        //    // Запрашиваемая функция выполнена успешно.
        //    {
        //        try
        //        {
        //            return SerializeResponse(response);
        //        }
        //        catch (Exception ex)
        //        // Злая ошибка сериализации ответа. Аналогично ошибке 500.
        //        {
        //            // Прервать отладку.
        //            DebugOnly.Break();

        //            // TODO залогировать.
        //            Debug.WriteLine(ex);

        //            // Вернуть результат с ошибкой.
        //            response = new ResponseMessage(requestContext, new InternalErrorResult("Internal Server Error"));
        //        }

        //        // response содержит ошибку.
        //        return SerializeResponse(response);
        //    }
        //}

        private static async ValueTask<ResponseMessage> WaitForInvokeControllerAsync(ValueTask<object?> task, RequestToInvoke requestContext)
        {
            object? rawResult;
            try
            {
                // Находит и выполняет запрашиваемую функцию.
                rawResult = await task.ConfigureAwait(false);
            }
            catch (BadRequestException ex)
            {
                // Вернуть результат с ошибкой.
                return new ResponseMessage(requestContext, new BadRequestResult(ex.Message));
            }
            catch (Exception ex)
            // Злая ошибка обработки запроса. Аналогично ошибке 500.
            {
                // Прервать отладку.
                DebugOnly.Break();

                Debug.WriteLine(ex);

                // Вернуть результат с ошибкой.
                return new ResponseMessage(requestContext, new InternalErrorResult("Internal Server Error"));
            }

            return new ResponseMessage(requestContext, rawResult);
        }

        /// <summary>
        /// AggressiveInlining.
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (!IsDisposed)
                return;

            throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// Не позволять начинать новый запрос если происходит остановка.
        /// AggressiveInlining.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfShutdownRequired()
        {
            if (_shutdownRequest == null)
                return;

            throw new WasShutdownException(_shutdownRequest);
        }

        /// <summary>
        /// Потокобезопасно освобождает ресурсы соединения. Вызывается при закрытии соединения.
        /// Взводит <see cref="Completion"/>.
        /// </summary>
        /// <param name="possibleReason">Возможная причина обрыва соединения.</param>
        private void AtomicDispose(CloseReason possibleReason)
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            // Только один поток может зайти сюда (за всю жизнь экземпляра).
            // Это настоящая причина обрыва соединения.
            {
                // Лучше выполнить в первую очередь.
                _sendChannel.Writer.TryComplete();

                // Передать исключение всем ожидающим потокам.
                _pendingRequests.PropagateExceptionAndLockup(possibleReason.ToException());

                // Закрыть соединение.
                _ws.Dispose();

                // Синхронизироваться с подписчиками на событие Disconnected.
                EventHandler<SocketDisconnectedEventArgs> disconnected;
                lock (DisconnectEventObj)
                {
                    // Запомнить истинную причину обрыва.
                    DisconnectReason = possibleReason;

                    // Установить флаг после причины обрыва.
                    _isConnected = false;

                    // Скопируем делегат что-бы вызывать не в блокировке — на всякий случай.
                    disconnected = _disconnected;

                    // Теперь можно безопасно убрать подписчиков.
                    _disconnected = null;
                }

                // Сообщить об обрыве.
                disconnected?.Invoke(this, new SocketDisconnectedEventArgs(this, possibleReason));

                // Установить Task Completion.
                SetCompletion(possibleReason);
            }
        }

        private void SetCompletion(CloseReason closeReason)
        {
            // Установить Task Completion.
            if(_completionTcs.TrySetResult(closeReason))
            {
                try
                {
                    _cts.Cancel(false);
                }
                catch (AggregateException ex)
                // Нужна защита от пользовательских ошибок.
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
            AtomicDispose(CloseReason.FromException(new ObjectDisposedException(GetType().FullName), _shutdownRequest, "Пользователь вызвал Dispose."));
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
            if(disposing)
            {
                DisposeManaged();
            }
        }
    }
}
