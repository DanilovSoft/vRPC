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
using DanilovSoft.vRPC.DTO;
using DanilovSoft.vRPC.Source;
using System.Text.Json;

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
        private protected abstract IConcurrentDictionary<MethodInfo, RequestMethodMeta> InterfaceMethods { get; }
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
        private readonly PendingRequestDictionary _pendingRequests;
        public EndPoint LocalEndPoint { get; }
        public EndPoint RemoteEndPoint { get; }
        /// <summary>
        /// Отправка сообщений <see cref="SerializedMessageToSend"/> должна выполняться только через этот канал.
        /// </summary>
        private readonly Channel<SerializedMessageToSend> _sendChannel;
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
        /// Если значение – <see langword="false"/>, то можно узнать причину через свойство <see cref="DisconnectReason"/>.
        /// Когда значение становится <see langword="false"/>, то вызывается событие <see cref="Disconnected"/>.
        /// После разъединения текущий экземпляр не может быть переподключен.
        /// </summary>
        /// <remarks><see langword="volatile"/>.</remarks>
        public bool IsConnected => _isConnected;
        public abstract bool IsAuthenticated { get; }
        private Task? _senderTask;

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
            ProtoBuf.Serializer.PrepareSerializer<MultipartHeaderDto>();
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
            //_pipe = new Pipe();

            _pendingRequests = new PendingRequestDictionary();

            // IoC готов к работе.
            ServiceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _invokeActions = actions;

            _sendChannel = Channel.CreateUnbounded<SerializedMessageToSend>(new UnboundedChannelOptions
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
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, this); // Без замыкания.
#else
            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, this, preferLocal: false); // Через глобальную очередь.
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
                var timeoutTask = Task.Delay(stopRequired.ShutdownTimeout);

                // Подождать грациозную остановку.
                if (await Task.WhenAny(_completionTcs.Task, timeoutTask).ConfigureAwait(false) == timeoutTask)
                {
                    Debug.WriteLine($"Достигнут таймаут {(int)stopRequired.ShutdownTimeout.TotalSeconds} сек. на грациозную остановку.");
                }

                // Не бросает исключения.
                TryDispose(CloseReason.FromException(new VRpcWasShutdownException(stopRequired)));

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
        /// <exception cref="VRpcWasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal Task<T> OnInterfaceMethodCall<T>(MethodInfo targetMethod, string? controllerName, object[] args)
        {
            // Создаём запрос для отправки.
            var requestMeta = InterfaceMethods.GetOrAdd(targetMethod, (tm, cn) => new RequestMethodMeta(tm, typeof(T), cn), controllerName);

            // Сериализуем запрос в память.
            SerializedMessageToSend serMsg = requestMeta.SerializeRequest(args);
            if (!requestMeta.IsNotificationRequest)
            {
                // Отправляем запрос.
                Task<T> pendingRequestTask = SendSerializedRequestAndWaitResponse<T>(requestMeta, serMsg);
                return pendingRequestTask;
                //return ConvertRequestTask(requestMeta, pendingRequestTask);
            }
            else
            {
                PostNotification(serMsg);
                return ConvertTaskToDefaultResultTask<T>(Task.CompletedTask);
                //return ConvertNotificationTask(requestMeta, Task.CompletedTask);
            }
        }


        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// Возвращает Task или готовый результат если был вызван синхронный метод.
        /// </summary>
        /// <exception cref="Exception"/>
        [SuppressMessage("Reliability", "CA2000:Ликвидировать объекты перед потерей области", Justification = "Анализатор не видит смену ответственности на Channel")]
        internal static Task<T> OnClientInterfaceCall<T>(ValueTask<ClientSideConnection> connectionTask, MethodInfo targetMethod, string? controllerName, object[] args)
        {
            // Создаём запрос для отправки.
            var methodMeta = ClientSideConnection.InterfaceMethodsInfo.GetOrAdd(targetMethod, 
                factory: (mi, cn) => new RequestMethodMeta(mi, typeof(T), cn), controllerName);

            // Сериализуем запрос в память. Лучше выполнить до завершения подключения.
            SerializedMessageToSend serMsg = methodMeta.SerializeRequest(args);
            SerializedMessageToSend? serMsgToDispose = serMsg;

            try
            {
                // Результатом может быть незавершённый таск.
                // Может начать отправку текущим потоком. Диспозит serMsg в случае ошибки.
                Task<T> task = ExecuteRequestStatic<T>(connectionTask, serMsg, methodMeta);

                serMsgToDispose = null; // Предотвратить Dispose.

                // Преждевременно проверим что возвращаемый тип подходит для типа интерфейса.
                //DebugOnlyValidateIfaceTypeMatch<T>(targetMethod, task);

                return task;
            }
            finally
            {
                // В случае исключения в методе ExecuteRequestStatic
                // объект может быть уже уничтожен но это не страшно, его Dispose - атомарный.
                serMsgToDispose?.Dispose();
            }
        }

        //[Conditional("DEBUG")]
        //private static void DebugOnlyValidateIfaceTypeMatch<T>(MethodInfo targetMethod, Task<T> activeCall)
        //{
        //    if (targetMethod.ReturnType != typeof(void) && activeCall != null)
        //    {
        //        Debug.Assert(targetMethod.ReturnType.IsInstanceOfType(activeCall), "Тип результата не соответствует возвращаемому типу интерфейса");
        //    }
        //}

        /// <summary>
        /// Отправляет запрос и возвращает результат. Результатом может быть Task, Task&lt;T&gt;, ValueTask, ValueTask&lt;T&gt; или готовый результат.
        /// </summary>
        private static Task<T> ExecuteRequestStatic<T>(ValueTask<ClientSideConnection> connectionTask, SerializedMessageToSend serMsg, RequestMethodMeta requestMeta)
        {
            if (!requestMeta.IsNotificationRequest)
            // Запрос должен получить ответ.
            {
                return SendRequestAndGetResultStatic<T>(connectionTask, serMsg, requestMeta);
            }
            else
            // Отправляем запрос как уведомление которое не ждёт ответ.
            {
                return SendNotificationStatic<T>(connectionTask, serMsg, requestMeta);
            }
        }

        /// <summary>
        /// Отправляет запрос и возвращает результат. Результатом может быть Task, Task&lt;T&gt;, ValueTask, ValueTask&lt;T&gt; или готовый результат.
        /// </summary>
        private static Task<T> SendRequestAndGetResultStatic<T>(ValueTask<ClientSideConnection> connectionTask, SerializedMessageToSend serMsg, RequestMethodMeta requestMeta)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            // Отправляем запрос.
            Task<T> pendingRequestTask = SendRequestAndGetResultAsync<T>(connectionTask, serMsg, requestMeta);

            return pendingRequestTask;

            // Результатом может быть не завершённый Task или готовый результат.
            //return ConvertRequestTask(requestMeta, pendingRequestTask);
        }

        /// <summary>
        /// Отправляет запрос как уведомление не ожидая ответа.
        /// </summary>
        private static Task<T> SendNotificationStatic<T>(ValueTask<ClientSideConnection> connectionTask, SerializedMessageToSend serMsg, RequestMethodMeta methodMeta)
        {
            Debug.Assert(methodMeta.IsNotificationRequest);

            // Может бросить исключение.
            // Добавляет сообщение в очередь на отправку.
            Task sendNotificationTask = SendNotificationStaticAsync(connectionTask, serMsg);
            
            return ConvertTaskToDefaultResultTask<T>(sendNotificationTask);

            // Конвертируем Task в ValueTask если это требует интерфейс.
            //return ConvertNotificationTask(methodMeta, sendNotificationTask);
        }

        private static Task<T> ConvertTaskToDefaultResultTask<T>(Task task)
        {
            return task.ContinueWith<T>(_ => default!,
                default,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Ожидает завершение подключения к серверу и передаёт сообщение в очередь на отправку.
        /// Может бросить исключение.
        /// Чаще всего возвращает <see cref="Task.CompletedTask"/>.
        /// </summary>
        /// <exception cref="VRpcWasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private static Task SendNotificationStaticAsync(ValueTask<ClientSideConnection> connectingTask, SerializedMessageToSend serializedMessage)
        {
            if (connectingTask.IsCompleted)
            {
                // Может бросить исключение.
                ManagedConnection connection = connectingTask.Result;
                
                // Отправляет уведомление через очередь.
                connection.PostNotification(serializedMessage);

                // Нотификации не возвращают результат.
                return Task.CompletedTask;
            }
            else
            // Подключение к серверу ещё не завершено.
            {
                return WaitForConnectAndSendNotification(connectingTask, serializedMessage);
            }

            // Локальная функция.
            static async Task WaitForConnectAndSendNotification(ValueTask<ClientSideConnection> conTask, SerializedMessageToSend serializedMessage)
            {
                ClientSideConnection connection = await conTask.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                connection.PostNotification(serializedMessage);
            }
        }

        private static Task<T> SendRequestAndGetResultAsync<T>(ValueTask<ClientSideConnection> connectionTask, SerializedMessageToSend serMsg, RequestMethodMeta requestMetadata)
        {
            if (connectionTask.IsCompleted)
            {
                // Может быть исключение если не удалось подключиться.
                ClientSideConnection connection = connectionTask.Result;

                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendSerializedRequestAndWaitResponse<T>(requestMetadata, serMsg);
            }
            else
            {
                return WaitForConnectAndSendRequest(connectionTask, serMsg, requestMetadata).Unwrap();
            }

            static async Task<Task<T>> WaitForConnectAndSendRequest(ValueTask<ClientSideConnection> t, SerializedMessageToSend serMsg, RequestMethodMeta requestMetadata)
            {
                ClientSideConnection connection = await t.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendSerializedRequestAndWaitResponse<T>(requestMetadata, serMsg);
            }
        }

        ///// <summary>
        ///// Может вернуть Null или Task или ValueTask.
        ///// </summary>
        //private static object? ConvertNotificationTask(RequestMethodMeta requestActionMeta, Task sendNotificationTask)
        //{
        //    if (requestActionMeta.IsAsync)
        //    // Возвращаемый тип функции интерфейса — Task или ValueTask.
        //    {
        //        // Сконвертировать в ValueTask если такой тип у интерфейса.
        //        // Не бросает исключения.
        //        object convertedTask = EncapsulateValueTask(sendNotificationTask, requestActionMeta.ReturnType);

        //        // Результатом может быть не завершённый Task (или ValueTask).
        //        return convertedTask;
        //    }
        //    else
        //    // Была вызвана синхронная функция.
        //    {
        //        // Результатом может быть исключение.
        //        sendNotificationTask.GetAwaiter().GetResult();

        //        // У уведомлений нет результата.
        //        return null;
        //    }
        //}

        ///// <summary>
        ///// Преобразует <see cref="Task"/><see langword="&lt;object?&gt;"/> в строгий тип <see cref="Task{T}"/>
        ///// или извлекает TResult из таска если метод интерфейса является синхронной функцией.
        ///// </summary>
        ///// <returns>Может быть незавершённый таск или TResult или Null.</returns>
        //private protected static object? ConvertRequestTask(RequestMethodMeta requestMeta, Task<object?> pendingRequestTask)
        //{
        //    if (requestMeta.IsAsync)
        //    // Возвращаемый тип функции интерфейса — Task.
        //    {
        //        if (requestMeta.ReturnType.IsGenericType)
        //        // У задачи есть результат.
        //        {
        //            // Task<object> должен быть преобразован в Task<T> иначе не получится вернуть через интерфейс.
        //            // Не бросает исключения.
        //            return TaskConverter.ConvertTask(pendingRequestTask, requestMeta.IncapsulatedReturnType, requestMeta.ReturnType);
        //        }
        //        else
        //        {
        //            if (requestMeta.ReturnType != typeof(ValueTask))
        //            // Возвращаемый тип интерфейса – Task.
        //            {
        //                // Можно вернуть как Task<object>.
        //                return pendingRequestTask;
        //            }
        //            else
        //            // Возвращаемый тип интерфейса – ValueTask.
        //            {
        //                return new ValueTask(task: pendingRequestTask);
        //            }
        //        }
        //    }
        //    else
        //    // Была вызвана синхронная функция.
        //    {
        //        // Результатом может быть исключение.
        //        object? finalResult = pendingRequestTask.GetAwaiter().GetResult();
        //        return finalResult;
        //    }
        //}

        ///// <summary>
        ///// Возвращает Task или ValueTask для соответствия типу <paramref name="returnType"/>.
        ///// Не бросает исключения.
        ///// </summary>
        ///// <param name="voidTask">Такс без результата (<see langword="void"/>).</param>
        //private static object EncapsulateValueTask(Task voidTask, Type returnType)
        //{
        //    if (returnType != typeof(ValueTask))
        //    // Возвращаемый тип интерфейса – Task.
        //    {
        //        // Конвертировать не нужно.
        //        return voidTask;
        //    }
        //    else
        //    // Возвращаемый тип интерфейса – ValueTask.
        //    {
        //        return new ValueTask(voidTask);
        //    }
        //}

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу. 
        /// Добавляет запрос-уведомление в очередь на отправку (выполнит отправку текущим потоком если очередь пуста).
        /// </summary>
        /// <exception cref="VRpcWasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal void PostNotification(SerializedMessageToSend serializedMessage)
        {
            ThrowIfDisposed();
            ThrowIfShutdownRequired();
            //ValidateAuthenticationRequired(requestMeta);

            // Планируем отправку запроса.
            TryPostMessage(serializedMessage);
        }

        /// <summary>
        /// Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// <exception cref="VRpcWasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private protected Task<T> SendRequestAndWaitResponse<T>(RequestMethodMeta requestMeta, object[] args)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            SerializedMessageToSend serMsgRequest = requestMeta.SerializeRequest(args);
            SerializedMessageToSend? serMsgToDispose = serMsgRequest;
            try
            {
                Task<T> pendingRequestTask = SendSerializedRequestAndWaitResponse<T>(requestMeta, serMsgRequest);
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
        /// <exception cref="VRpcWasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        /// <returns>Таск с результатом от сервера.</returns>
        private protected Task<T> SendSerializedRequestAndWaitResponse<T>(RequestMethodMeta requestMeta, SerializedMessageToSend serMsg)
        {
            Debug.Assert(!requestMeta.IsNotificationRequest);

            SerializedMessageToSend? serMsgToDispose = serMsg;
            try
            {
                ThrowIfDisposed();
                ThrowIfShutdownRequired();

                // Добавить запрос в словарь для дальнейшей связки с ответом.
                ResponseAwaiter<T> responseAwaiter = _pendingRequests.AddRequest<T>(requestMeta, out int uid);

                // Назначить запросу уникальный идентификатор.
                serMsg.Uid = uid;

                // Планируем отправку запроса.
                // Не бросает исключения.
                TryPostMessage(serMsg);

                // Мы больше не владеем этим объектом.
                serMsgToDispose = null;

                // Ожидаем результат от потока поторый читает из сокета.
                return WaitForAwaiterAsync(responseAwaiter);
            }
            finally
            {
                serMsgToDispose?.Dispose();
            }

            static async Task<T> WaitForAwaiterAsync(ResponseAwaiter<T> responseAwaiter)
            {
                // Ожидаем результат от потока поторый читает из сокета.
                // Валидным результатом может быть исключение.
                T value = await responseAwaiter;

                // Успешно получили результат без исключений.
                return value;
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
                    try
                    {
                        // Читаем фрейм веб-сокета.
                        webSocketMessage = await _ws.ReceiveExAsync(headerBuffer.AsMemory(bufferOffset), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    // Обрыв соединения.
                    {
                        // Оповестить об обрыве и завершить поток.
                        TryDispose(CloseReason.FromException(ex, _shutdownRequest));
                        return;
                    }

                    bufferOffset += webSocketMessage.Count;

                } while (!webSocketMessage.EndOfMessage);

                #endregion

                #region Десериализуем хедер

                HeaderDto? header;
                if (webSocketMessage.MessageType == Ms.WebSocketMessageType.Binary)
                {
                    try
                    {
                        header = HeaderDto.DeserializeProtobuf(headerBuffer, 0, webSocketMessage.Count);
                    }
                    catch (Exception headerException)
                    // Не удалось десериализовать заголовок.
                    {
                        // Отправка Close и завершить поток.
                        await SendCloseHeaderErrorAsync(webSocketMessage.Count, headerException).ConfigureAwait(false);
                        return;
                    }
                }
                else
                // Получен Close или Text.
                {
                    await TryNonBinaryTypeCloseAndDisposeAsync(webSocketMessage).ConfigureAwait(false);
                    return;
                }
                #endregion

                if (header != null)
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
                        if (!TryProcessPayload(header, contentMem))
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
        private bool TryProcessPayload(HeaderDto header, ReadOnlyMemory<byte> payload)
        {
            if (header.IsRequest)
            // Получен запрос.
            {
                if (TryIncreaseActiveRequestsCount(header))
                {
                    if (ValidateHeader(header))
                    {
                        if (TryGetRequestMethod(header, out ControllerActionMeta? action))
                        {
                            RequestToInvoke? requestToInvoke = null;
                            try
                            {
                                #region Десериализация запроса

                                if (RequestContentParser.TryDeserializeRequest(payload, action, header, out requestToInvoke, out IActionResult? error))
                                {
                                    #region Выполнение запроса

                                    Debug.Assert(requestToInvoke != null, "Не может быть Null когда success");

                                    // Начать выполнение запроса в отдельном потоке.
                                    StartProcessRequest(requestToInvoke);

                                    requestToInvoke = null; // Предотвратить Dispose.

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
                                        Debug.Assert(header.Uid != null, "Не может быть Null когда IsResponseRequired");

                                        // Передать на отправку результат с ошибкой через очередь.
                                        PostSendResponse(new ResponseMessage(header.Uid.Value, error));
                                    }

                                    // Вернуться к чтению следующего сообщения.
                                    return true;

                                    #endregion
                                }
                                #endregion
                            }
                            finally
                            {
                                requestToInvoke?.Dispose();
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

                Debug.Assert(header.Uid != null, "У ответа на запрос должен быть идентификатор");

                // Удалить запрос из словаря.
                if (header.Uid != null && _pendingRequests.TryRemove(header.Uid.Value, out IResponseAwaiter? respAwaiter))
                // Передать ответ ожидающему потоку.
                {
                    respAwaiter.SetResponse(header, payload);

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

        private bool TryGetRequestMethod(HeaderDto header, [NotNullWhen(true)] out ControllerActionMeta? action)
        {
            Debug.Assert(header.ActionName != null);

            if (_invokeActions.TryGetAction(header.ActionName, out action))
            {
                return true;
            }
            else
            // Не найден метод контроллера.
            {
                if (header.IsResponseRequired)
                // Запрос ожидает ответ.
                {
                    Debug.Assert(header.Uid != null);

                    var error = MethodNotFound(header.ActionName);

                    // Передать на отправку результат с ошибкой через очередь.
                    PostSendResponse(new ResponseMessage(header.Uid.Value, error));
                }
                return false;
            }
        }

        private static NotFoundResult MethodNotFound(string actionName)
        {
            int controllerIndex = actionName.IndexOf(GlobalVars.ControllerNameSplitter, StringComparison.Ordinal);

            if (controllerIndex > 0)
            {
                return new NotFoundResult($"Unable to find requested action \"{actionName}\".");
            }
            else
            {
                return new NotFoundResult($"Controller name not specified in request \"{actionName}\".");
            }
        }

        /// <returns>true если хедер валиден.</returns>
        private bool ValidateHeader(HeaderDto header)
        {
            if (!header.IsRequest || !string.IsNullOrEmpty(header.ActionName))
            {
                return true;
            }
            else
            // Запрос ожидает ответ.
            {
                Debug.Assert(header.Uid != null, "Не может быть Null когда IsResponseRequired");

                // Передать на отправку результат с ошибкой через очередь.
                PostSendResponse(new ResponseMessage(header.Uid.Value, new InvalidRequestResult("В заголовке запроса отсутствует имя метода")));

                return false;
            }
        }

        /// <summary>
        /// Увеличивает счётчик активных запросов.
        /// </summary>
        /// <param name="header"></param>
        /// <returns>true если разрешено продолжить выполнение запроса, 
        /// false если требуется остановить сервис.</returns>
        private bool TryIncreaseActiveRequestsCount(HeaderDto header)
        {
            if (header.IsResponseRequired)
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
                    TryDispose(CloseReason.FromException(ex, _shutdownRequest));
                    return;
                }
            }
            TryDisposeOnCloseReceived();
        }

        //private Task SendCloseMessageTypeNotSupportedAsync()
        //{
        //    // Тип фрейма должен быть Binary.
        //    var protocolErrorException = new RpcProtocolErrorException(SR.TextMessageTypeNotSupported);

        //    // Отправка Close.
        //    return CloseAndDisposeAsync(protocolErrorException, SR.TextMessageTypeNotSupported);
        //}

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
            // Тип фрейма должен быть Binary.
            var protocolErrorException = new VRpcProtocolErrorException(SR.TextMessageTypeNotSupported);

            // Отправка Close.
            return TryCloseAndDisposeAsync(protocolErrorException, SR.TextMessageTypeNotSupported);
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
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        /// <param name="protocolErrorException">Распространяет исключение ожидаюшим потокам.</param>
        private async Task TryCloseAndDisposeAsync(Exception protocolErrorException, string closeDescription)
        {
            // Сообщить потокам что обрыв произошел по вине удалённой стороны.
            _pendingRequests.TryPropagateExceptionAndLockup(protocolErrorException);

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
            ThreadPool.UnsafeQueueUserWorkItem(SerializeResponseAndTrySendThreadEntryPoint, (this, responseToSend));
#else
            ThreadPool.UnsafeQueueUserWorkItem(SerializeResponseAndTrySendThreadEntryPoint, (this, responseToSend), preferLocal: false); // Предпочитаем глобальную очередь.
#endif
        }

#if NETSTANDARD2_0 || NET472

        // Точка входа потока тред-пула.
        /// <remarks>Не бросает исключения.</remarks>
        private static void SerializeResponseAndTrySendThreadEntryPoint(object? state)
        {
            Debug.Assert(state != null);
            var tuple = ((ManagedConnection, ResponseMessage))state!;
            SerializeResponseAndTrySendThreadEntryPoint(argState: tuple);
        }
#endif
        // Точка входа потока тред-пула.
        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private static void SerializeResponseAndTrySendThreadEntryPoint((ManagedConnection self, ResponseMessage responseToSend) argState)
        {
            // Сериализуем.
            SerializedMessageToSend serializedMessage = SerializeResponse(argState.responseToSend);

            // Ставим в очередь.
            argState.self.TryPostMessage(serializedMessage);
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
                    var actionContext = new ActionContext(responseToSend.ActionMeta, serMsg.MemPoolStream);

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
                        responseToSend.ActionMeta.SerializerDelegate(serMsg.MemPoolStream, responseToSend.ActionResult);
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
        /// Сериализует хэдер в стрим сообщения. Не бросает исключения.
        /// </summary>
        private static void AppendHeader(SerializedMessageToSend messageToSend)
        {
            HeaderDto header = CreateHeader(messageToSend);

            // Записать заголовок в конец стрима. Не бросает исключения.
            header.SerializeProtoBuf(messageToSend.MemPoolStream, out int headerSize);

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

                return HeaderDto.FromResponse(responseToSend.Uid, messageToSend.StatusCode.Value, (int)messageToSend.MemPoolStream.Length, messageToSend.ContentEncoding);
            }
            else
            // Создать хедер для нового запроса.
            {
                var request = messageToSend.MessageToSend as RequestMethodMeta;
                Debug.Assert(request != null);

                return HeaderDto.CreateRequest(messageToSend.Uid, (int)messageToSend.MemPoolStream.Length, messageToSend.ContentEncoding, request.ActionFullName);
            }
        }

        /// <summary>
        /// Добавляет хэдер и передает на отправку другому потоку если канал ещё не закрыт.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void TryPostMessage(SerializedMessageToSend serializedMessage)
        {
            Debug.Assert(serializedMessage != null);

            SerializedMessageToSend? serializedMessageToDispose = serializedMessage;
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
                        serializedMessageToDispose = null;
                    }
                }
            }
            finally
            {
                serializedMessageToDispose?.Dispose();
            }
        }

        /// <summary>
        /// Принимает заказы на отправку и отправляет в сокет. Запускается из конструктора. Не бросает исключения.
        /// </summary>
        /// <remarks>Как и для ReceiveLoop здесь не должно быть глубокого async стека.</remarks>
        private async Task LoopSendAsync() // Точка входа нового потока.
        {
            while (!IsDisposed)
            {
                // Ждём сообщение для отправки.
                if (await _sendChannel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    // Всегда true — у нас только один читатель.
                    _sendChannel.Reader.TryRead(out SerializedMessageToSend serializedMessage);

                    Debug.Assert(serializedMessage != null);

                    // Теперь мы владеем этим объектом.
                    using (serializedMessage)
                    {
                        //  Увеличить счетчик активных запросов.
                        if (TryIncreaseActiveRequestsCount(serializedMessage))
                        {
                            LogSend(serializedMessage);

                            byte[] streamBuffer = serializedMessage.MemPoolStream.GetBuffer();

                            // Размер сообщения без заголовка.
                            int messageSize = (int)serializedMessage.MemPoolStream.Length - serializedMessage.HeaderSize;

                            #region Отправка заголовка

                            try
                            {
                                // Заголовок лежит в конце стрима.
                                await SendBufferAsync(streamBuffer.AsMemory(messageSize, serializedMessage.HeaderSize), true).ConfigureAwait(false);
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
                                        await SendBufferAsync(streamBuffer.AsMemory(0, messageSize), true).ConfigureAwait(false);
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
                            if (TryDecreaseActiveRequestsCount(serializedMessage))
                            {

                            }
                            else
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
                else
                // Другой поток закрыл канал.
                {
                    // Завершить поток.
                    return;
                }
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
            return _ws.SendAsync(buffer, Ms.WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);
        }

        [Conditional("LOG_RPC")]
        private static void LogSend(SerializedMessageToSend serializedMessage)
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
            if (header != null)
            {
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
        }

        /// <summary>
        /// Вызывает запрошенный метод контроллера и возвращает результат.
        /// Результатом может быть IActionResult или Raw объект или исключение.
        /// </summary>
        /// <exception cref="Exception">Исключение пользователя.</exception>
        /// <exception cref="ObjectDisposedException"/>
        /// <param name="receivedRequest">Гарантированно выполнит Dispose.</param>
        /// <returns><see cref="IActionResult"/> или любой объект.</returns>
        private ValueTask<object?> InvokeControllerAsync(RequestToInvoke receivedRequest)
        {
            RequestToInvoke? requestToDispose = receivedRequest;
            IServiceScope? scopeToDispose = null;
            try
            {
                // Проверить доступ к функции.
                if (ActionPermissionCheck(receivedRequest.ActionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user))
                {
                    IServiceScope scope = ServiceProvider.CreateScope();
                    scopeToDispose = scope;

                    // Инициализируем Scope текущим соединением.
                    var getProxyScope = scope.ServiceProvider.GetService<GetProxyScope>();
                    getProxyScope.GetProxy = this;

                    // Активируем контроллер через IoC.
                    var controller = scope.ServiceProvider.GetRequiredService(receivedRequest.ActionMeta.ControllerType) as Controller;
                    Debug.Assert(controller != null);

                    // Подготавливаем контроллер.
                    controller.BeforeInvokeController(this, user);

                    //BeforeInvokeController(controller);

                    // Вызов метода контроллера.
                    // (!) Результатом может быть не завершённый Task.
                    object? actionResult = receivedRequest.ActionMeta.FastInvokeDelegate(controller, receivedRequest.Args);

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
                            requestToDispose = null;

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
                requestToDispose?.Dispose();

                // ServiceScope выполнит Dispose всем созданным экземплярам.
                scopeToDispose?.Dispose();
            }

            static async ValueTask<object?> WaitForControllerActionAsync(ValueTask<object?> task, IServiceScope scope, RequestToInvoke pendingRequest)
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

        //private protected abstract void PrepareController();

        /// <summary>
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private protected abstract bool ActionPermissionCheck(ControllerActionMeta actionMeta, out IActionResult? permissionError, out ClaimsPrincipal? user);

        /// <summary>
        /// В новом потоке выполняет запрос и отправляет ему результат или ошибку.
        /// </summary>
        private void StartProcessRequest(RequestToInvoke request)
        {
#if NETSTANDARD2_0 || NET472
            ThreadPool.UnsafeQueueUserWorkItem(StartProcessRequestThread, (this, request)); // Без замыкания.
#else
            // Предпочитаем глобальную очередь что-бы не замедлять читающий поток.
            ThreadPool.UnsafeQueueUserWorkItem(StartProcessRequestThread, (this, request), preferLocal: false);
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

        // Точка входа для потока из пула.
        [DebuggerStepThrough]
        private static void StartProcessRequestThread((ManagedConnection self, RequestToInvoke request) stateTuple)
        {
            stateTuple.self.ProcessRequestThreadEntryPoint(stateTuple.request);
        }

        /// <summary>
        /// Выполняет запрос и отправляет результат или ошибку.
        /// </summary>
        /// <remarks>Точка входа потока из пула. Необработанные исключения пользователя крашнут процесс.</remarks>
        private void ProcessRequestThreadEntryPoint(RequestToInvoke requestContext)
        {
            // Сокет может быть уже закрыт, например по таймауту,
            // в этом случае ничего выполнять не нужно.
            if (!IsDisposed)
            {
                ProcessRequest(requestContext);
            }
            else
            {
                requestContext.Dispose();
            }
        }

        /// <summary>
        /// Увеличивает счётчик на 1 при получении запроса или при отправке запроса.
        /// </summary>
        /// <returns>True если можно продолжить получение запросов иначе нужно закрыть соединение.</returns>
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
            {
                return false;
            }
        }

        /// <summary>
        /// Уменьшает счётчик активных запросов на 1 при получении ответа на запрос или при отправке ответа на запрос.
        /// </summary>
        /// <returns>True если можно продолжить получение запросов иначе нужно остановить сервис.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool DecreaseActiveRequestsCount()
        {
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

        /// <exception cref="Exception">Ошибка сериализации пользовательских данных.</exception>
        private void ProcessRequest(RequestToInvoke requestToInvoke)
        {
            if (requestToInvoke.IsResponseRequired)
            {
                Debug.Assert(requestToInvoke.Uid != null);

                ValueTask<object?> pendingRequestTask;

                // Этот блок Try должен быть идентичен тому который чуть ниже — для асинхронной обработки.
                try
                {
                    // Выполняет запрос и возвращает результат.
                    // Может быть исключение пользователя.
                    pendingRequestTask = InvokeControllerAsync(requestToInvoke);
                }
                catch (VRpcBadRequestException ex)
                {
                    // Вернуть результат с ошибкой.
                    SendBadRequest(requestToInvoke, ex);
                    return;
                }
                catch (Exception ex)
                // Злая ошибка обработки запроса. Аналогично ошибке 500.
                {
                    // Прервать отладку.
                    //DebugOnly.Break();

                    // Вернуть результат с ошибкой.
                    SendInternalerverError(requestToInvoke, ex);
                    return;
                }

                if (pendingRequestTask.IsCompletedSuccessfully)
                // Результат контроллера получен синхронно.
                {
                    // Не бросает исключения.
                    object? actionResult = pendingRequestTask.Result;

                    SendOkResponse(requestToInvoke, actionResult);
                }
                else
                // Результат контроллера — асинхронный таск.
                {
                    WaitResponseAndSendAsync(pendingRequestTask, requestToInvoke);

                    // TO THINK ошибки в таске можно обработать и не провоцируя исключения.
                    // ContinueWith должно быть в 5 раз быстрее. https://stackoverflow.com/questions/51923100/try-catchoperationcanceledexception-vs-continuewith
                    async void WaitResponseAndSendAsync(ValueTask<object?> task, RequestToInvoke requestToInvoke)
                    {
                        Debug.Assert(requestToInvoke.Uid != null);

                        object? actionResult;
                        // Этот блок Try должен быть идентичен тому который чуть выше — для синхронной обработки.
                        try
                        {
                            actionResult = await task.ConfigureAwait(false);
                        }
                        catch (VRpcBadRequestException ex)
                        {
                            // Вернуть результат с ошибкой.
                            SendBadRequest(requestToInvoke, ex);
                            return;
                        }
                        catch (Exception ex)
                        // Злая ошибка обработки запроса. Аналогично ошибке 500.
                        {
                            // Прервать отладку.
                            //DebugOnly.Break();

                            // Вернуть результат с ошибкой.
                            SendInternalerverError(requestToInvoke, ex);
                            return;
                        }
                        SendOkResponse(requestToInvoke, actionResult);
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

        private void SendOkResponse(RequestToInvoke requestToInvoke, object? actionResult)
        {
            Debug.Assert(requestToInvoke.Uid != null);

            SerializeResponseAndTrySend(new ResponseMessage(requestToInvoke.Uid.Value, requestToInvoke.ActionMeta, actionResult));
        }

        private void SendInternalerverError(RequestToInvoke requestToInvoke, Exception exception)
        {
            Debug.Assert(requestToInvoke.Uid != null);

            // Вернуть результат с ошибкой.
            SerializeResponseAndTrySend(new ResponseMessage(requestToInvoke.Uid.Value, requestToInvoke.ActionMeta, new InternalErrorResult("Internal Server Error")));
        }

        private void SendBadRequest(RequestToInvoke requestToInvoke, VRpcBadRequestException exception)
        {
            Debug.Assert(requestToInvoke.Uid != null);

            // Вернуть результат с ошибкой.
            SerializeResponseAndTrySend(new ResponseMessage(requestToInvoke.Uid.Value, requestToInvoke.ActionMeta, new BadRequestResult(exception.Message)));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>Не бросает исключения.</remarks>
        private void ProcessNotificationRequest(RequestToInvoke notificationRequest)
        {
            Debug.Assert(notificationRequest.IsResponseRequired == false);

            ValueTask<object?> pendingRequestTask;
            try
            {
                // Может быть исключение пользователя.
                pendingRequestTask = InvokeControllerAsync(notificationRequest);
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

        ///// <summary>
        ///// Выполняет запрос и инкапсулирует результат в <see cref="ResponseMessage"/>.
        ///// </summary>
        ///// <exception cref="Exception">Могут быть исключения пользователя.</exception>
        //private ValueTask<ResponseMessage> InvokeControllerAndGetResponseAsync(RequestToInvoke requestToInvoke)
        //{
        //    Debug.Assert(requestToInvoke.Uid != null);

        //    // Может быть исключение пользователя.
        //    ValueTask<object?> pendingRequestTask = InvokeControllerAsync(requestToInvoke);

        //    if (pendingRequestTask.IsCompletedSuccessfully)
        //    // Синхронно только в случае успеха.
        //    {
        //        // Результат контроллера.
        //        object? actionResult = pendingRequestTask.Result;

        //        return new ValueTask<ResponseMessage>(new ResponseMessage(requestToInvoke.Uid.Value, requestToInvoke.ActionMeta, actionResult));
        //    }
        //    else
        //    {
        //        return WaitForInvokeControllerAsync(pendingRequestTask, requestToInvoke);

        //        static async ValueTask<ResponseMessage> WaitForInvokeControllerAsync(ValueTask<object?> task, RequestToInvoke requestToInvoke)
        //        {
        //            Debug.Assert(requestToInvoke.Uid != null);

        //            object? actionResult = await task.ConfigureAwait(false);
                    
        //            return new ResponseMessage(requestToInvoke.Uid.Value, requestToInvoke.ActionMeta, actionResult: actionResult);
        //        }
        //    }
        //}

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
        /// </summary>
        /// <remarks>AggressiveInlining.</remarks>
        /// <exception cref="VRpcWasShutdownException"/>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfShutdownRequired()
        {
            if (_shutdownRequest == null)
                return;

            throw new VRpcWasShutdownException(_shutdownRequest);
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
            if(_completionTcs.TrySetResult(closeReason))
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
            if(disposing)
            {
                DisposeManaged();
            }
        }
    }
}
