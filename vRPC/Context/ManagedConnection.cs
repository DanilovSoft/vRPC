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
        private protected abstract IConcurrentDictionary<MethodInfo, RequestToSend> InterfaceMethods { get; }
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly InvokeActionsDictionary _invokeActions;
        /// <summary>
        /// Для <see cref="Task"/> <see cref="Completion"/>.
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
        public CloseReason DisconnectReason { get; private set; }
        /// <summary>
        /// Возвращает <see cref="Task"/> который завершается когда 
        /// соединение переходит в закрытое состояние.
        /// Возвращает <see cref="DisconnectReason"/>.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion => _completionTcs.Task;
        public bool IsServer { get; }
        public ServiceProvider ServiceProvider { get; }
        /// <summary>
        /// Подключенный TCP сокет.
        /// </summary>
        private readonly ManagedWebSocket _socket;
        /// <summary>
        /// Коллекция запросов ожидающие ответ от удалённой стороны.
        /// </summary>
        private readonly RequestQueue _pendingRequests;
        public EndPoint LocalEndPoint => _socket.LocalEndPoint;
        public EndPoint RemoteEndPoint => _socket.RemoteEndPoint;
        /// <summary>
        /// Отправка сообщения <see cref="SerializedMessageToSend"/> должна выполняться только через этот канал.
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
        private volatile StopRequired _stopRequired;
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
        private EventHandler<SocketDisconnectedEventArgs> _disconnected;
        /// <summary>
        /// Событие обрыва соединения. Может сработать только один раз.
        /// Если подписка на событие происходит к уже отключенному сокету то событие сработает сразу же.
        /// Гарантирует что событие не будет пропущено в какой бы момент не происходила подписка.
        /// </summary>
        public event EventHandler<SocketDisconnectedEventArgs> Disconnected
        {
            add
            {
                CloseReason closeReason = null;
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
                    value(this, new SocketDisconnectedEventArgs(closeReason));
                }
            }
            remove
            {
                // Отписываться можно без блокировки — делегаты потокобезопасны.
                _disconnected -= value;
            }
        }
        private protected abstract void BeforeInvokeController(Controller controller);
        private volatile bool _isConnected = true;
        /// <summary>
        /// Является <see langword="volatile"/>. Если значение – <see langword="false"/>, то можно узнать причину через свойство <see cref="DisconnectReason"/>.
        /// Когда значение становится <see langword="false"/>, то вызывается событие <see cref="Disconnected"/>.
        /// После разъединения текущий экземпляр не может быть переподключен.
        /// </summary>
        public bool IsConnected => _isConnected;

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
        internal ManagedConnection(ManagedWebSocket clientConnection, bool isServer, ServiceProvider serviceProvider, InvokeActionsDictionary controllers)
        {
            IsServer = isServer;

            _socket = clientConnection;
            _pendingRequests = new RequestQueue();

            // IoC готов к работе.
            ServiceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _invokeActions = controllers;

            _sendChannel = Channel.CreateUnbounded<SerializedMessageToSend>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true, // Внимательнее с этим параметром!
                SingleReader = true,
                SingleWriter = false,
            });

            // Не может сработать сразу потому что пока не запущен 
            // поток чтения или отправки – некому спровоцировать событие.
            _socket.Disconnecting += WebSocket_Disconnected;
        }

        /// <summary>
        /// Запускает бесконечный цикл обработки запросов.
        /// </summary>
        internal void InitStartThreads()
        {
            // Запустить цикл отправки сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(SenderLoopStart, this); // Без замыкания.

            // Запустить цикл приёма сообщений.
            ThreadPool.UnsafeQueueUserWorkItem(ReceiveLoopStart, this); // Без замыкания.
        }

        private static void SenderLoopStart(object state)
        {
            // Не бросает исключения.
            ((ManagedConnection)state).SenderLoop();
        }

        private static void ReceiveLoopStart(object state)
        {
            // Не бросает исключения.
            ((ManagedConnection)state).ReceiveLoop();
        }

        private void WebSocket_Disconnected(object sender, SocketDisconnectingEventArgs e)
        {
            CloseReason closeReason;
            if (e.DisconnectingReason.Gracifully)
            {
                closeReason = CloseReason.FromCloseFrame(e.DisconnectingReason.CloseStatus, 
                    e.DisconnectingReason.CloseDescription, e.DisconnectingReason.AdditionalDescription, _stopRequired);
            }
            else
            {
                closeReason = CloseReason.FromException(e.DisconnectingReason.Error, 
                    _stopRequired, e.DisconnectingReason.AdditionalDescription);
            }
            AtomicDispose(closeReason);
        }

        /// <summary>
        /// Запрещает отправку новых запросов; Ожидает когда завершатся текущие запросы 
        /// и отправляет удалённой стороне сообщение о закрытии соединения с ожиданием подтверджения.
        /// Затем выполняет Dispose, взводит <see cref="Completion"/> и 
        /// возвращает <see langword="true"/> если остановка завершилась раньше таймаута.
        /// Не бросает исключения.
        /// Потокобезопасно.
        /// </summary>
        internal async Task<CloseReason> StopAsync(StopRequired stopRequired)
        {
            bool firstTime;
            lock (StopRequiredLock)
            {
                if (_stopRequired == null)
                {
                    firstTime = true;

                    // Запретить выполнять новые запросы.
                    // Запомнить причину отключения что-бы позднее передать её удалённой стороне.
                    _stopRequired = stopRequired; // volatile.

                    if (!DecActiveRequestCount())
                    // Нет ни одного ожадающего запроса.
                    {
                        // Можно безопасно остановить сокет.
                        // Не бросает исключения.
                        SendCloseAsync(stopRequired.CloseDescription).GetAwaiter();
                    }
                    // Иначе другие потоки уменьшив переменную увидят что флаг стал -1
                    // Это будет соглашением о необходимости остановки.
                }
                else
                {
                    firstTime = false;
                    stopRequired = _stopRequired;
                }
            }

            if (firstTime)
            {
                // Подождать грациозную остановку.
                await Task.WhenAny(Completion, Task.Delay(stopRequired.DisconnectTimeout)).ConfigureAwait(false);

                // Не бросает исключения.
                AtomicDispose(CloseReason.FromException(new StopRequiredException(stopRequired)));

                CloseReason closeReason = Completion.Result;

                // Передать результат другим потокам которые вызовут Stop.
                return stopRequired.SetTaskResult(closeReason);
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
        private void CloseReceived()
        {
            // Был получен Close. Это значит что веб сокет уже закрыт и нам остаётся только закрыть сервис.
            AtomicDispose(CloseReason.FromCloseFrame(_socket.CloseStatus, _socket.CloseStatusDescription, null, _stopRequired));
        }

        /// <summary>
        /// Отправляет сообщение Close и ожидает ответный Close. Затем закрывает соединение.
        /// Не бросает исключения.
        /// </summary>
        private async Task SendCloseAsync(string closeDescription)
        {
            // Эту функцию вызывает тот поток который поймал флаг о необходимости завершения сервиса.

            try
            {
                // Отправить Close с ожиданием ответного Close.
                // Может бросить исключение если сокет уже в статусе Close.
                await _socket.CloseAsync(Ms.WebSocketCloseStatus.NormalClosure, closeDescription, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Оповестить об обрыве.
                AtomicDispose(CloseReason.FromException(ex, _stopRequired));

                // Завершить поток.
                return;
            }

            // Благодаря событию WebSocket.Disconnect у нас гарантированно вызовется AtomicDispose.
        }

        /// <summary>
        /// Отправляет сообщение Close и ожидает ответный Close. Затем закрывает соединение.
        /// Не бросает исключения.
        /// </summary>
        private Task SendCloseBeforeStopAsync()
        {
            Debug.Assert(_stopRequired != null);

            return SendCloseAsync(_stopRequired.CloseDescription);
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        /// <exception cref="StopRequiredException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal object OnServerProxyCall(MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Создаём запрос для отправки.
            RequestToSend requestToSend = InterfaceMethods.GetOrAdd(targetMethod, (tm, cn) => new RequestToSend(tm, cn), controllerName);

            // Сериализуем запрос в память.
            SerializedMessageToSend serMsg = SerializeRequest(requestToSend, args);

            if (!requestToSend.Notification)
            {
                // Отправляем запрос.
                Task<object> taskObject = SendRequestAndGetResult(serMsg, requestToSend);

                return ConvertRequestTask(requestToSend, taskObject);
            }
            else
            {
                SendNotification(serMsg);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        internal static object OnClientProxyCallStatic(ValueTask<ManagedConnection> contextTask, MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Создаём запрос для отправки.
            RequestToSend requestToSend = ClientSideConnection.InterfaceMethodsInfo.GetOrAdd(targetMethod, (mi, cn) => new RequestToSend(mi, cn), controllerName);

            // Сериализуем запрос в память. Лучше выполнить до подключения.
            SerializedMessageToSend serMsg = SerializeRequest(requestToSend, args);

            try
            {
                if (!requestToSend.Notification)
                {
                    // Отправляем запрос.
                    Task<object> taskObject = SendRequestWithResultAsync(contextTask, serMsg, requestToSend);

                    // Может бросить исключение.
                    object convertedResult = ConvertRequestTask(requestToSend, taskObject);

                    // Предотвратить Dispose.
                    serMsg = null;

                    // Результатом может быть не завершённый Task.
                    return convertedResult;
                }
                else
                {
                    // Может бросить исключение.
                    // Добавляет сообщение в очередь на отправку.
                    Task connectionTask = SendNotificationAsync(contextTask, serMsg);

                    if (requestToSend.IsAsync)
                    // Возвращаемый тип функции интерфейса — Task или ValueTask.
                    {
                        // Предотвратить Dispose.
                        serMsg = null;

                        // Сконвертировать в ValueTask если такой тип у интерфейса.
                        // Не бросает исключения.
                        object convertedTask = ConvertNotificationTask(requestToSend, connectionTask);

                        // Результатом может быть не завершённый Task.
                        return convertedTask;
                    }
                    else
                    // Была вызвана синхронная функция.
                    {
                        // Результатом может быть исключение.
                        connectionTask.GetAwaiter().GetResult();

                        // Предотвратить Dispose.
                        serMsg = null;

                        // Возвращаемый тип интерфейсы — void.
                        return null;
                    }
                }
            }
            finally
            {
                serMsg?.Dispose();
            }
        }

        /// <summary>
        /// Ожидает завершение подключения к серверу и передаёт сообщение в очередь на отправку.
        /// Может бросить исключение.
        /// </summary>
        /// <exception cref="StopRequiredException"/>
        /// <exception cref="ObjectDisposedException"/>
        private static Task SendNotificationAsync(ValueTask<ManagedConnection> connectingTask, SerializedMessageToSend serializedMessage)
        {
            if (connectingTask.IsCompleted)
            {
                // Может бросить исключение.
                ManagedConnection connection = connectingTask.Result;
                
                // Отправляет уведомление.
                connection.SendNotification(serializedMessage);

                // Нотификации не возвращают результат.
                return Task.CompletedTask;
            }
            else
            // Подключение к серверу ещё не завершено.
            {
                return WaitForConnectAndSendNotification(connectingTask, serializedMessage);
            }

            // Локальная функция.
            static async Task WaitForConnectAndSendNotification(ValueTask<ManagedConnection> t, SerializedMessageToSend serializedMessage)
            {
                ManagedConnection connection = await t.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                connection.SendNotification(serializedMessage);
            }
        }

        private static Task<object> SendRequestWithResultAsync(ValueTask<ManagedConnection> contextTask, SerializedMessageToSend serializedMessage, RequestToSend requestMessage)
        {
            if(contextTask.IsCompleted)
            {
                // Может быть исключение если не удалось подключиться.
                ManagedConnection connection = contextTask.Result;
                
                // Отправляет запрос и получает результат от удалённой стороны.
                return connection.SendRequestAndGetResult(serializedMessage, requestMessage);
            }
            else
            {
                return WaitForConnectAndSendRequest(contextTask, serializedMessage, requestMessage);
            }

            static async Task<object> WaitForConnectAndSendRequest(ValueTask<ManagedConnection> t, SerializedMessageToSend serializedMessage, RequestToSend requestMessage)
            {
                ManagedConnection connection = await t.ConfigureAwait(false);
                
                // Отправляет запрос и получает результат от удалённой стороны.
                return await connection.SendRequestAndGetResult(serializedMessage, requestMessage).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Преобразует <see cref="Task"/><see langword="&lt;object&gt;"/> в <see cref="Task{T}"/> или возвращает TResult
        /// если метод интерфейса является синхронной функцией.
        /// </summary>
        private static object ConvertRequestTask(RequestToSend requestToSend, Task<object> taskObject)
        {
            if (requestToSend.IsAsync)
            // Возвращаемый тип функции интерфейса — Task.
            {
                if (requestToSend.MethodInfo.ReturnType.IsGenericType)
                // У задачи есть результат.
                {
                    // Task<object> должен быть преобразован в Task<T>.
                    // Не бросает исключения.
                    return TaskConverter.ConvertTask(taskObject, requestToSend.IncapsulatedReturnType, requestToSend.MethodInfo.ReturnType);
                }
                else
                {
                    if (requestToSend.MethodInfo.ReturnType != typeof(ValueTask))
                    // Возвращаемый тип интерфейса – Task.
                    {
                        // Можно вернуть как Task<object>.
                        return taskObject;
                    }
                    else
                    // Возвращаемый тип интерфейса – ValueTask.
                    {
                        return new ValueTask(taskObject);
                    }
                }
            }
            else
            // Была вызвана синхронная функция.
            {
                // Результатом может быть исключение.
                object finalResult = taskObject.GetAwaiter().GetResult();
                return finalResult;
            }
        }

        /// <summary>
        /// Конвертирует Task в ValueTask если этого требует интерфейс.
        /// Не бросает исключения.
        /// </summary>
        /// <param name="requestToSend"></param>
        /// <param name="taskObject">Задача с возвращаемым типом <see langword="void"/>.</param>
        /// <returns></returns>
        private static object ConvertNotificationTask(RequestToSend requestToSend, Task taskObject)
        {
            if (requestToSend.MethodInfo.ReturnType != typeof(ValueTask))
            // Возвращаемый тип интерфейса – Task.
            {
                // Конвертировать не нужно.
                return taskObject;
            }
            else
            // Возвращаемый тип интерфейса – ValueTask.
            {
                return new ValueTask(taskObject);
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу. Отправляет запрос-уведомление.
        /// </summary>
        /// <exception cref="StopRequiredException"/>
        /// <exception cref="ObjectDisposedException"/>
        internal void SendNotification(SerializedMessageToSend serializedMessage)
        {
            ThrowIfDisposed();
            ThrowIfStopRequired();

            // Планируем отправку запроса.
            QueueSendMessage(serializedMessage);
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу. Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// <exception cref="StopRequiredException"/>
        /// <exception cref="ObjectDisposedException"/>
        private Task<object> SendRequestAndGetResult(SerializedMessageToSend serializedMessage, RequestToSend requestMessage)
        {
            try
            {
                ThrowIfDisposed();
                ThrowIfStopRequired();

                // Добавить запрос в словарь для дальнейшей связки с ответом.
                RequestAwaiter tcs = _pendingRequests.AddRequest(requestMessage, out int uid);

                // Назначить запросу уникальный идентификатор.
                serializedMessage.Uid = uid;

                // Планируем отправку запроса.
                // Не бросает исключения.
                QueueSendMessage(serializedMessage);

                // Предотвратить Dispose на месте.
                serializedMessage = null;

                // Ожидаем результат от потока поторый читает из сокета.
                return WaitForAwaiterAsync(tcs);
            }
            finally
            {
                serializedMessage?.Dispose();
            }

            static async Task<object> WaitForAwaiterAsync(RequestAwaiter tcs)
            {
                // Ожидаем результат от потока поторый читает из сокета.
                // Валидным результатом может быть исключение.
                object rawResult = await tcs;

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

                ValueWebSocketReceiveResult webSocketMessage;

                int bufferOffset = 0;
                do
                {
                    try
                    {
                        // Читаем фрейм веб-сокета.
                        webSocketMessage = await _socket.ReceiveExAsync(headerBuffer.AsMemory(bufferOffset), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    // Обрыв соединения.
                    {
                        // Оповестить об обрыве.
                        AtomicDispose(CloseReason.FromException(ex, _stopRequired));

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
                    if(_socket.State == Ms.WebSocketState.CloseReceived)
                    {
                        try
                        {
                            await _socket.CloseOutputAsync(_socket.CloseStatus.Value, _socket.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDispose(CloseReason.FromException(ex, _stopRequired));

                            // Завершить поток.
                            return;
                        }
                    }

                    CloseReceived();

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
                                    webSocketMessage = await _socket.ReceiveExAsync(contentBuffer, CancellationToken.None).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве.
                                    AtomicDispose(CloseReason.FromException(ex, _stopRequired));

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
                                    if (_socket.State == Ms.WebSocketState.CloseReceived)
                                    {
                                        try
                                        {
                                            await _socket.CloseOutputAsync(_socket.CloseStatus.Value, _socket.CloseStatusDescription, CancellationToken.None).ConfigureAwait(false);
                                        }
                                        catch (Exception ex)
                                        // Обрыв соединения.
                                        {
                                            // Оповестить об обрыве.
                                            AtomicDispose(CloseReason.FromException(ex, _stopRequired));

                                            // Завершить поток.
                                            return;
                                        }
                                    }

                                    CloseReceived();

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

                            RequestToInvoke requestToInvoke;
                            IActionResult error = null;
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
                                        object rawResult;
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
                                                    $"Ошибка десериализации ответа на запрос \"{reqAwaiter.Request.ActionName}\".", deserializationException);

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
                                    await SendCloseBeforeStopAsync().ConfigureAwait(false);

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

        /// <summary>
        /// Отправляет Close и выполняет Dispose.
        /// </summary>
        /// <param name="protocolErrorException">Распространяет исключение ожидаюшим потокам.</param>
        private async Task CloseAndDisposeAsync(Exception protocolErrorException, string closeDescription)
        {
            // Сообщить потокам что обрыв произошел по вине удалённой стороны.
            _pendingRequests.PropagateExceptionAndLockup(protocolErrorException);

            try
            {
                // Отключаемся от сокета с небольшим таймаутом.
                using (var cts = new CancellationTokenSource(2000))
                    await _socket.CloseAsync(Ms.WebSocketCloseStatus.ProtocolError, closeDescription, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            // Злой обрыв соединения.
            {
                // Оповестить об обрыве.
                AtomicDispose(CloseReason.FromException(ex, _stopRequired));

                // Завершить поток.
                return;
            }

            // Оповестить об обрыве.
            AtomicDispose(CloseReason.FromException(protocolErrorException, _stopRequired));

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
            ThreadPool.UnsafeQueueUserWorkItem(QueueSendResponseThread, Tuple.Create(this, responseToSend));
        }

        private static void QueueSendResponseThread(object state)
        {
            var tuple = (Tuple<ManagedConnection, ResponseMessage>)state;

            // Сериализуем.
            SerializedMessageToSend serializedMessage = SerializeResponse(tuple.Item2);
            
            // Ставим в очередь.
            tuple.Item1.QueueSendMessage(serializedMessage);
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        private static SerializedMessageToSend SerializeRequest(RequestToSend requestToSend, object[] args)
        {
            var serializedMessage = new SerializedMessageToSend(requestToSend);
            try
            {
                var request = new RequestMessageDto(requestToSend.ActionName, args);
                ExtensionMethods.SerializeObjectJson(serializedMessage.MemPoolStream, request);

                var ret = serializedMessage;
                serializedMessage = null;
                return ret;
            }
            finally
            {
                serializedMessage?.Dispose();
            }
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        private static SerializedMessageToSend SerializeResponse(ResponseMessage responseToSend)
        {
            var serMsg = new SerializedMessageToSend(responseToSend);
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

                    object result = responseToSend.ActionResult;
                    if (result != null)
                    {
                        responseToSend.ReceivedRequest.ActionToInvoke.Serializer(serMsg.MemPoolStream, result);
                        serMsg.ContentEncoding = responseToSend.ReceivedRequest.ActionToInvoke.ProducesEncoding;
                    }
                }
            }
            catch
            {
                serMsg.Dispose();
                throw;
            }
            return serMsg;
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
                return HeaderDto.CreateRequest(messageToSend.Uid, (int)messageToSend.MemPoolStream.Length);
            }
        }

        /// <summary>
        /// Добавляет хэдер и передает на отправку другому потоку.
        /// Не бросает исключения.
        /// </summary>
        private void QueueSendMessage(SerializedMessageToSend messageToSend)
        {
            Debug.Assert(messageToSend != null);

            try
            {
                // На текущем этапе сокет может быть уже уничтожен другим потоком.
                // В этом случае можем беспоследственно проигнорировать отправку; вызывающий получит исключение через RequestAwaiter.
                if (!IsDisposed)
                {
                    // Сериализуем хедер. Не бросает исключения.
                    AppendHeader(messageToSend);

                    // Передать на отправку.
                    // Из-за AllowSynchronousContinuations частично начнёт отправку текущим потоком(!).
                    if (_sendChannel.Writer.TryWrite(messageToSend))
                    // Канал ещё не закрыт (не был вызван Dispose).
                    {
                        // Предотвратить Dispose на месте.
                        messageToSend = null;
                        return;
                    }
                }
            }
            finally
            {
                messageToSend?.Dispose();
            }
        }

        /// <summary>
        /// Принимает заказы на отправку и отправляет в сокет. Запускается из конструктора. Не бросает исключения.
        /// </summary>
        /// <returns></returns>
        private async void SenderLoop() // Точка входа нового потока.
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
                            await _socket.SendAsync(streamBuffer.AsMemory(messageSize, serializedMessage.HeaderSize),
                                Ms.WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDispose(CloseReason.FromException(ex, _stopRequired));

                            // Завершить поток.
                            return;
                        }
                        #endregion

                        if (messageSize > 0)
                        {
                            #region Отправка тела сообщения (запрос или ответ на запрос)

                            try
                            {
                                await _socket.SendAsync(streamBuffer.AsMemory(0, messageSize),
                                    Ms.WebSocketMessageType.Binary, true, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDispose(CloseReason.FromException(ex, _stopRequired));

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
                                await SendCloseBeforeStopAsync().ConfigureAwait(false);

                                // Завершить поток.
                                return;
                            }
                        }
                    }
                }
                else
                // Dispose закрыл канал.
                {
                    // Завершить поток.
                    return;
                }
            }
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
        private ValueTask<object> InvokeControllerAsync(RequestToInvoke receivedRequest)
        {
            // Проверить доступ к функции.
            if (InvokeMethodPermissionCheck(receivedRequest.ActionToInvoke.TargetMethod, receivedRequest.ActionToInvoke.ControllerType, out IActionResult permissionError))
            {
                IServiceScope scope = ServiceProvider.CreateScope();
                try
                {
                    // Инициализируем Scope текущим соединением.
                    var getProxyScope = scope.ServiceProvider.GetService<GetProxyScope>();
                    getProxyScope.GetProxy = this;

                    // Активируем контроллер через IoC.
                    var controller = (Controller)scope.ServiceProvider.GetRequiredService(receivedRequest.ActionToInvoke.ControllerType);

                    // Подготавливаем контроллер.
                    BeforeInvokeController(controller);

                    // Вызов метода контроллера.
                    object controllerResult = receivedRequest.ActionToInvoke.FastInvokeDelegate(controller, receivedRequest.Args);

                    // Может быть не завершённый Task.
                    if (controllerResult != null)
                    {
                        ValueTask<object> t = DynamicAwaiter.WaitAsync(controllerResult);

                        if (t.IsCompletedSuccessfully)
                        {
                            // Извлекает результат из Task'а.
                            controllerResult = t.Result;

                            // Результат успешно получен без исключения.
                            return new ValueTask<object>(controllerResult);
                        }
                        else
                        {
                            // Предотвратить Dispose.
                            scope = null;

                            return WaitForInvokeControllerAsync(t, scope);
                        }
                    }
                    else
                    {
                        return new ValueTask<object>(result: null);
                    }
                }
                finally
                {
                    // ServiceScope выполнит Dispose всем созданным экземплярам.
                    scope?.Dispose();
                }
            }
            else
                return new ValueTask<object>(permissionError);
        }

        private static async ValueTask<object> WaitForInvokeControllerAsync(ValueTask<object> t, IServiceScope scope)
        {
            using (scope)
            {
                object result = await t.ConfigureAwait(false);

                // Результат успешно получен без исключения.
                return result;
            }
        }

        /// <summary>
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// Не бросает исключения.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        protected abstract bool InvokeMethodPermissionCheck(MethodInfo method, Type controllerType, out IActionResult permissionError);

        /// <summary>
        /// В новом потоке выполняет запрос и отправляет ему результат или ошибку.
        /// </summary>
        private void StartProcessRequest(RequestToInvoke request)
        {
            ThreadPool.UnsafeQueueUserWorkItem(StartProcessRequestThread, Tuple.Create(this, request)); // Без замыкания.
        }

        private static void StartProcessRequestThread(object state)
        {
            var tuple = (Tuple<ManagedConnection, RequestToInvoke>)state;

            // Не бросает исключения.
            tuple.Item1.StartProcessRequestThread(tuple.Item2);
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
                // Выполняет запрос и возвращает ответ.
                ValueTask<ResponseMessage> t = GetResponseAsync(requestToInvoke);

                if (t.IsCompleted)
                {
                    // Не бросает исключения.
                    ResponseMessage responseMessage = t.Result;

                    // Не бросает исключения.
                    SerializeAndSendResponse(responseMessage, requestToInvoke);
                }
                else
                {
                    WaitResponseAndSendAsync(t, requestToInvoke);
                }
            }
            else
            // Notification
            {
                ValueTask<object> t = InvokeControllerAsync(requestToInvoke);

                if(!t.IsCompletedSuccessfully)
                {
                    WaitForNotification(t);
                }
            }
        }

        private static async void WaitForNotification(ValueTask<object> t)
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
        private void SerializeAndSendResponse(ResponseMessage responseMessage, RequestToInvoke requestContext)
        {
            // Не бросает исключения.
            SerializedMessageToSend responseToSend = SerializeResponse(responseMessage, requestContext);

            // Не бросает исключения.
            QueueSendMessage(responseToSend);
        }

        private async void WaitResponseAndSendAsync(ValueTask<ResponseMessage> t, RequestToInvoke requestContext)
        {
            // Не бросает исключения.
            // Выполняет запрос и возвращает ответ.
            ResponseMessage responseMessage = await t.ConfigureAwait(false);

            // Не бросает исключения.
            SerializeAndSendResponse(responseMessage, requestContext);
        }

        /// <summary>
        /// Выполняет запрос клиента и инкапсулирует результат в <see cref="ResponseMessage"/>.
        /// Не бросает исключения.
        /// </summary>
        private ValueTask<ResponseMessage> GetResponseAsync(RequestToInvoke requestContext)
        {
            // Не должно бросать исключения.
            ValueTask<object> t = InvokeControllerAsync(requestContext);

            if(t.IsCompletedSuccessfully)
            // Синхронно только в случае успеха.
            {
                // Результат контроллера. Может быть Task.
                object result = t.Result;

                return new ValueTask<ResponseMessage>(new ResponseMessage(requestContext, result));
            }
            else
            {
                return WaitForInvokeControllerAsync(t, requestContext);
            }
        }

        private static SerializedMessageToSend SerializeResponse(ResponseMessage response, RequestToInvoke requestContext)
        {
            if (response == null)
            // Запрашиваемая функция выполнена успешно.
            {
                try
                {
                    return SerializeResponse(response);
                }
                catch (Exception ex)
                // Злая ошибка сериализации ответа. Аналогично ошибке 500.
                {
                    // Прервать отладку.
                    DebugOnly.Break();

                    // TODO залогировать.
                    Debug.WriteLine(ex);

                    // Вернуть результат с ошибкой.
                    response = new ResponseMessage(requestContext, new InternalErrorResult("Internal Server Error"));
                }

                // response содержит ошибку.
                return SerializeResponse(response);
            }
            else
            // response содержит ошибку.
            {
                // Сериализуется без исключения.
                return SerializeResponse(response);
            }
        }

        private static async ValueTask<ResponseMessage> WaitForInvokeControllerAsync(ValueTask<object> t, RequestToInvoke requestContext)
        {
            object rawResult;
            try
            {
                // Находит и выполняет запрашиваемую функцию.
                rawResult = await t.ConfigureAwait(false);
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
        /// <exception cref="StopRequiredException"/>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfStopRequired()
        {
            if (_stopRequired == null)
                return;

            throw new StopRequiredException(_stopRequired);
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
                _socket.Dispose();

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
                disconnected?.Invoke(this, new SocketDisconnectedEventArgs(possibleReason));

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
        T IGetProxy.GetProxy<T>() => InnerGetProxy<T>();

        private protected abstract T InnerGetProxy<T>();

        protected virtual void DisposeManaged()
        {
            AtomicDispose(CloseReason.FromException(new ObjectDisposedException(GetType().FullName), _stopRequired, "Пользователь вызвал Dispose."));
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
