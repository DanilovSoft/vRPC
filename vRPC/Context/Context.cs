using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using DynamicMethodsLib;
using MyWebSocket = DanilovSoft.WebSocket.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;
using DanilovSoft.WebSocket;
using System.Net.Sockets;
using System.Net;
using System.Runtime.CompilerServices;

namespace vRPC
{
    /// <summary>
    /// Контекст соединения Web-Сокета. Владеет соединением.
    /// </summary>
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public class Context : IDisposable
    {
        /// <summary>
        /// Максимальный размер фрейма который может передавать протокол. Сообщение может быть фрагментированно фреймами размером не больше этого значения.
        /// </summary>
        private const int WebSocketMaxFrameSize = 4096;
        private const string ProtocolHeaderErrorMessage = "Произошла ошибка десериализации заголовка от удалённой стороны.";
        /// <summary>
        /// Содержит имена методов прокси интерфейса без постфикса Async.
        /// </summary>
        private static readonly Dictionary<MethodInfo, string> _proxyMethodName = new Dictionary<MethodInfo, string>();
        /// <summary>
        /// Потокобезопасный словарь используемый только для чтения.
        /// Хранит все доступные контроллеры. Не учитывает регистр.
        /// </summary>
        private readonly Dictionary<string, Type> _controllers;
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly ControllerActionsDictionary _controllerActions;
        private readonly TaskCompletionSource<int> _connectedTcs = new TaskCompletionSource<int>();
        private readonly ProxyCache _proxyCache = new ProxyCache();
        public ServiceProvider ServiceProvider { get; private set; }
        /// <summary>
        /// Подключенный TCP сокет.
        /// </summary>
        private protected SocketWrapper Socket { get; private set; }
        public EndPoint LocalEndPoint => Socket.WebSocket.LocalEndPoint;
        public EndPoint RemoteEndPoint => Socket.WebSocket.RemoteEndPoint;
        /// <summary>
        /// Отправка сообщения <see cref="Message"/> должна выполняться только через этот канал.
        /// </summary>
        private readonly Channel<SendJob> _sendChannel;
        private int _disposed;
        private bool IsDisposed => Volatile.Read(ref _disposed) == 1;
        /// <summary>
        /// <see langword="true"/> если происходит остановка сервиса.
        /// </summary>
        private volatile bool _stopRequired;
        /// <summary>
        /// Возвращает <see cref="Task"/> который завершается когда сервис полностью 
        /// остановлен и больше не будет обрабатывать запросы. Не бросает исключения.
        /// </summary>
        public Task Completion => _connectedTcs.Task;
        /// <summary>
        /// Количество запросов для обработки и количество ответов для отправки.
        /// Для отслеживания грациозной остановки сервиса.
        /// </summary>
        private int _reqAndRespCount;

        /// <summary>
        /// Подписку на событие Disconnected нужно синхронизировать что-бы подписчики не пропустили момент обрыва.
        /// </summary>
        private readonly object _disconnectEventObj = new object();
        private EventHandler<SocketDisconnectedEventArgs> _Disconnected;
        /// <summary>
        /// Событие обрыва соединения. Может сработать только один раз.
        /// Если подписка на событие происходит к уже отключенному сокету то событие сработает сразу же.
        /// Гарантирует что событие не будет пропущено в какой бы момент не происходила подписка.
        /// </summary>
        public event EventHandler<SocketDisconnectedEventArgs> Disconnected
        {
            add
            {
                lock (_disconnectEventObj)
                {
                    if (_disconnectReason == null)
                    {
                        _Disconnected += value;
                    }
                    else
                    // Подписка к уже отключенному сокету.
                    {
                        value(this, new SocketDisconnectedEventArgs(_disconnectReason));
                    }
                }
            }
            remove
            {
                lock (_disconnectEventObj)
                {
                    _Disconnected -= value;
                }
            }
        }
        /// <summary>
        /// Истиная причина обрыва соединения. 
        /// <see langword="volatile"/> нужен только для публичного свойства <see cref="DisconnectReason"/> 
        /// так как <see cref="IsConnected"/> у нас тоже <see langword="volatile"/>.
        /// </summary>
        private volatile Exception _disconnectReason;
        /// <summary>
        /// Причина обрыва соединения.
        /// </summary>
        public Exception DisconnectReason => _disconnectReason;
        internal event EventHandler<Controller> BeforeInvokeController;
        private volatile bool _isConnected = true;
        /// <summary>
        /// <see langword="volatile"/>.
        /// </summary>
        public bool IsConnected => _isConnected;

        // static ctor.
        static Context()
        {
            // Прогрев сериализатора.
            ProtoBuf.Serializer.PrepareSerializer<HeaderDto>();
        }

        // ctor.
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ioc">Контейнер Listener'а.</param>
        internal Context(MyWebSocket clientConnection, ServiceProvider serviceProvider, Dictionary<string, Type> controllers)
        {
            // У сервера сокет всегда подключен и переподключаться не может.
            Socket = new SocketWrapper(clientConnection);

            // IoC готов к работе.
            ServiceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _controllers = controllers;

            _controllerActions = new ControllerActionsDictionary(controllers);

            // Запустить диспетчер отправки сообщений.
            _sendChannel = Channel.CreateUnbounded<SendJob>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true, // Внимательнее с этим параметром!
                SingleReader = true,
                SingleWriter = false,
            });

            // Может спровоцировать Disconnect раньше чем выполнится конструктор наследника.
            // Эта ситуация должна быть синхронизирована.
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                // Не бросает исключения.
                ((Context)state).SenderLoop();
            }, this); // Без замыкания.
        }

        /// <summary>
        /// Запрещает отправку новых запросов и приводит к остановке когда обработаются ожидающие запросы.
        /// </summary>
        internal void StopRequired()
        {
            _stopRequired = true;
            
            if(Interlocked.Decrement(ref _reqAndRespCount) == -1)
            // Нет ни одного ожадающего запроса.
            {
                // Можно безопасно остановить сокет.
                Dispose(new StopRequiredException());
                SetCompleted();
            }
            // Иначе другие потоки уменьшив переменную увидят что флаг стал -1
            // Это будет соглашением о необходимости остановки.
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// </summary>
        public T GetProxy<T>()
        {   
            return _proxyCache.GetProxy<T>(() => new ValueTask<Context>(this));
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        internal static object OnProxyCall(ValueTask<Context> contextTask, MethodInfo targetMethod, object[] args, string controllerName)
        {
            //#region CreateArgs()
            //Arg[] CreateArgs()
            //{
            //    ParameterInfo[] par = targetMethod.GetParameters();
            //    Arg[] retArgs = new Arg[par.Length];

            //    for (int i = 0; i < par.Length; i++)
            //    {
            //        ParameterInfo p = par[i];
            //        retArgs[i] = new Arg(p.Name, args[i]);
            //    }
            //    return retArgs;
            //}
            //#endregion

            // Без постфикса Async.
            string remoteMethodName = GetProxyMethodName(targetMethod);

            // Подготавливаем запрос для отправки.
            var requestToSend = Message.CreateRequest($"{controllerName}/{remoteMethodName}", args);

            // Тип результата инкапсулированный в Task<T>.
            Type resultType = GetActionReturnType(targetMethod);

            // Отправляет запрос и получает результат от удалённой стороны.
            Task<object> taskObject = OnProxyCallAsync(contextTask, requestToSend, resultType);

            // Если возвращаемый тип функции — Task.
            if (targetMethod.IsAsyncMethod())
            {
                // Если у задачи есть результат.
                if (targetMethod.ReturnType.IsGenericType)
                {
                    // Task<object> должен быть преобразован в Task<T>.
                    return TaskConverter.ConvertTask(taskObject, resultType, targetMethod.ReturnType);
                }
                else
                {
                    if (targetMethod.ReturnType != typeof(ValueTask))
                    {
                        // Если возвращаемый тип Task(без результата) то можно вернуть Task<object>.
                        return taskObject;
                    }
                    else
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

        private static async Task<object> OnProxyCallAsync(ValueTask<Context> contextTask, Message requestToSend, Type returnType)
        {
            Context context;
            if (contextTask.IsCompletedSuccessfully)
                context = contextTask.GetAwaiter().GetResult();
            else
                context = await contextTask.ConfigureAwait(false);
            
            object taskResult = await context.OnProxyCall(requestToSend, returnType).ConfigureAwait(false);
            return taskResult;
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        internal Task<object> OnProxyCall(Message requestToSend, Type resultType)
        {
            ThrowIfDisposed();
            ThrowIfStopRequired();

            // Отправляет запрос и получает результат от удалённой стороны.
            Task<object> taskObject = ExecuteRequestAsync(requestToSend, resultType);
            return taskObject;
        }

        /// <summary>
        /// Возвращает имя метода без постфикса Async.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private static string GetProxyMethodName(MethodInfo method)
        {
            lock(_proxyMethodName)
            {
                if (_proxyMethodName.TryGetValue(method, out string name))
                {
                    return name;
                }
                else
                {
                    name = method.TrimAsyncPostfix();
                    _proxyMethodName.Add(method, name);
                    return name;
                }
            }
        }

        /// <summary>
        /// Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// <param name="returnType">Тип в который будет десериализован результат запроса.</param>
        private protected async Task<object> ExecuteRequestAsync(Message requestToSend, Type returnType)
        {
            // Добавить запрос в словарь для дальнейшей связки с ответом.
            RequestAwaiter tcs = Socket.PendingRequests.AddRequest(returnType, requestToSend, out ushort uid);

            // Назначить запросу уникальный идентификатор.
            requestToSend.Uid = uid;

            // Планируем отправку запроса.
            QueueSendMessage(requestToSend, MessageType.Request);

            // Ожидаем результат от потока поторый читает из сокета.
            object rawResult = await tcs;

            // Успешно получили результат без исключений.
            return rawResult;
        }

        /// <summary>
        /// Запускает бесконечный цикл, в фоновом потоке, считывающий из сокета запросы и ответы.
        /// </summary>
        internal void StartReceivingLoop()
        {
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                // Не бросает исключения.
                ((Context)state).ReceiveLoop();
                
            }, this); // Без замыкания.
        }

        private async void ReceiveLoop()
        {
            byte[] headerBuffer = new byte[HeaderDto.HeaderMaxSize];

            // Бесконечно обрабатываем сообщения сокета.
            while (!IsDisposed)
            {
                #region Читаем хедер

                ValueWebSocketReceiveExResult webSocketMessage;

                try
                {
                    // Читаем фрейм веб-сокета.
                    webSocketMessage = await Socket.WebSocket.ReceiveExAsync(headerBuffer, CancellationToken.None);
                }
                catch (Exception ex)
                // Обрыв соединения.
                {
                    // Оповестить об обрыве.
                    AtomicDisconnect(ex);

                    // Завершить поток.
                    return;
                }

                HeaderDto header;
                if (webSocketMessage.ErrorCode == SocketError.Success)
                {
                    try
                    {
                        header = HeaderDto.DeserializeProtobuf(headerBuffer, 0, webSocketMessage.Count);
                    }
                    catch (Exception headerException)
                    // Не удалось десериализовать заголовок.
                    {
                        #region Отправка Close и выход

                        var protocolErrorException = new ProtocolErrorException(ProtocolHeaderErrorMessage, headerException);

                        // Сообщить потокам что обрыв произошел по вине удалённой стороны.
                        Socket.PendingRequests.PropagateExceptionAndLockup(protocolErrorException);

                        try
                        {
                            // Отключаемся от сокета с небольшим таймаутом.
                            using (var cts = new CancellationTokenSource(2000))
                                await Socket.WebSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "Ошибка десериализации заголовка.", cts.Token);
                        }
                        catch (Exception ex)
                        // Злой обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(ex);

                            // Завершить поток.
                            return;
                        }

                        // Оповестить об обрыве.
                        AtomicDisconnect(protocolErrorException);

                        // Завершить поток.
                        return;
                        #endregion
                    }
                }
                else
                {
                    // Оповестить об обрыве.
                    AtomicDisconnect(webSocketMessage.ErrorCode.ToException());

                    // Завершить поток.
                    return;
                }
                #endregion

                // Стрим который будет содержать сообщение целиком.
                using (var messageStream = new MemoryPoolStream(header.ContentLength))
                {
                    // Обязательно установить размер стрима. Можно не очищать – буффер будет перезаписан.
                    messageStream.SetLength(header.ContentLength, clear: false);

                    int receiveMessageBytesLeft = header.ContentLength;
                    int offset = 0;
                    byte[] messageStreamBuffer = messageStream.DangerousGetBuffer();

                    do // Читаем и склеиваем фреймы веб-сокета пока не EndOfMessage.
                    {
                        #region Пока не EndOfMessage записывать в буфер памяти

                        #region Читаем фрейм веб-сокета

                        try
                        {
                            // Читаем фрейм веб-сокета.
                            webSocketMessage = await Socket.WebSocket.ReceiveExAsync(
                                messageStreamBuffer.AsMemory().Slice(offset, receiveMessageBytesLeft), CancellationToken.None);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(ex);

                            // Завершить поток.
                            return;
                        }
                        #endregion

                        if (webSocketMessage.ErrorCode == SocketError.Success)
                        {
                            if (webSocketMessage.MessageType != WebSocketMessageType.Close)
                            {
                                offset += webSocketMessage.Count;
                                receiveMessageBytesLeft -= webSocketMessage.Count;
                            }
                            else
                            // Другая сторона закрыла соединение.
                            {
                                // Сформировать причину закрытия соединения.
                                string exceptionMessage = GetMessageFromCloseFrame();

                                // Сообщить потокам что удалённая сторона выполнила закрытие соединения.
                                AtomicDisconnect(new SocketClosedException(exceptionMessage));

                                // Завершить поток.
                                return;
                            }
                        }
                        else
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(webSocketMessage.ErrorCode.ToException());

                            // Завершить поток.
                            return;
                        }

                        #endregion

                    } while (!webSocketMessage.EndOfMessage);

                    if (header != null)
                    {
                        #region Обработка Payload

                        // Установить курсор в начало payload.
                        //messageStream.Position = 0;

                        if (header.StatusCode == StatusCode.Request)
                        // Получен запрос.
                        {
                            #region Выполнение запроса

                            #region Десериализация запроса

                            RequestMessageDto receivedRequest;
                            try
                            {
                                // Десериализуем запрос.
                                receivedRequest = ExtensionMethods.DeserializeRequestJson(messageStream);
                            }
                            catch (Exception ex)
                            // Ошибка десериализации запроса.
                            {
                                #region Игнорируем запрос

                                // Подготовить ответ с ошибкой.
                                var errorResponse = Message.FromResult(header.Uid, new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{ex.Message}\"."));

                                // Передать на отправку результат с ошибкой.
                                QueueSendMessage(errorResponse, MessageType.Response);

                                // Вернуться к получению заголовка.
                                continue;
                                #endregion
                            }
                            #endregion

                            #region Выполнение запроса

                            // Запрос успешно десериализован.
                            receivedRequest.Header = header;

                            // Установить контекст запроса.
                            receivedRequest.RequestContext = new RequestContext();

                            // Начать выполнение запроса в отдельном потоке.
                            StartProcessRequest(receivedRequest);
                            #endregion

                            #endregion
                        }
                        else
                        // Получен ответ на запрос.
                        {
                            #region Передача другому потоку ответа на запрос

                            // Удалить запрос из словаря.
                            if (Socket.PendingRequests.TryRemove(header.Uid, out RequestAwaiter reqAwaiter))
                            // Передать ответ ожидающему потоку.
                            {
                                #region Передать ответ ожидающему потоку

                                if (header.StatusCode == StatusCode.Ok)
                                // Запрос на удалённой стороне был выполнен успешно.
                                {
                                    #region Передать успешный результат

                                    if (reqAwaiter.ResultType != typeof(void))
                                    {
                                        // Десериализатор в соответствии с ContentEncoding.
                                        var deserializer = header.GetDeserializer();

                                        bool deserialized;
                                        object rawResult = null;
                                        try
                                        {
                                            rawResult = deserializer(messageStream, reqAwaiter.ResultType);
                                            deserialized = true;
                                        }
                                        catch (Exception deserializationException)
                                        {
                                            var protocolErrorException = new ProtocolErrorException(
                                                $"Ошибка десериализации ответа на запрос \"{reqAwaiter.Request.ActionName}\".", deserializationException);

                                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удаленной стороны.
                                            reqAwaiter.TrySetException(protocolErrorException);

                                            deserialized = false;
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
                                    string errorMessage = messageStream.ReadAsString();

                                    // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                                    reqAwaiter.TrySetException(new BadRequestException(errorMessage, header.StatusCode));
                                }
                                #endregion

                                // Получен ожидаемый ответ на запрос.
                                if (Interlocked.Decrement(ref _reqAndRespCount) == -1)
                                // Был запрос на остановку.
                                {
                                    SetCompleted();
                                    return;
                                }
                            }

                            #endregion
                        }
                        #endregion
                    }
                    else
                    // Хедер не получен.
                    {
                        #region Отправка Close

                        var protocolErrorException = new ProtocolErrorException("Удалённая сторона прислала недостаточно данных для заголовка.");

                        // Сообщить потокам что обрыв произошел по вине удалённой стороны.
                        Socket.PendingRequests.PropagateExceptionAndLockup(protocolErrorException);

                        try
                        {
                            // Отключаемся от сокета с небольшим таймаутом.
                            using (var cts = new CancellationTokenSource(2000))
                                await Socket.WebSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "Непредвиденное завершение потока данных.", cts.Token);
                        }
                        catch (Exception ex)
                        // Злой обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(ex);

                            // Завершить поток.
                            return;
                        }

                        // Оповестить об обрыве.
                        AtomicDisconnect(protocolErrorException);

                        // Завершить поток.
                        return;

                        #endregion
                    }
                }
            }
        }

        /// <summary>
        /// Сериализует сообщение и передает на отправку другому потоку.
        /// Не бросает исключения.
        /// </summary>
        private void QueueSendMessage(Message messageToSend, MessageType messageType)
        {
            // На текущем этапе сокет может быть уже уничтожен другим потоком
            // В результате чего в текущем потоке случилась ошибка но отправлять её не нужно.
            if (!Socket.IsDisposed)
            {
                // Сериализуем контент.
                #region Сериализуем сообщение

                var contentStream = new MemoryPoolStream();

                ActionContext actionContext = null;

                // Записать в стрим запрос или результат запроса.
                if (messageToSend.IsRequest)
                {
                    var request = new RequestMessageDto
                    {
                        ActionName = messageToSend.ActionName,
                        Args = messageToSend.Args,
                    };
                    //ExtensionMethods.SerializeObjectProtobuf(contentStream, request);
                    ExtensionMethods.SerializeObjectJson(contentStream, request);
                }
                else
                // Ответ на запрос.
                {
                    actionContext = new ActionContext(this, contentStream, messageToSend.ReceivedRequest?.RequestContext);

                    // Записать контент.
                    Execute(messageToSend.Result, actionContext);
                }

                // Размер контента.
                int contentLength = (int)contentStream.Length;

                // Готовим заголовок.
                var header = new HeaderDto(messageToSend.Uid, actionContext?.StatusCode ?? StatusCode.Request)
                {
                    ContentLength = contentLength,
                };

                if (actionContext != null)
                {
                    // Записать в заголовок формат контента.
                    header.ContentEncoding = actionContext.ProducesEncoding;
                }

                var mem = new MemoryPoolStream(contentLength);

                // Записать заголовок в самое начало.
                header.SerializeProtoBuf(mem, out int headerSize);

                byte[] buffer = contentStream.DangerousGetBuffer();
                mem.Write(buffer, 0, contentLength);
                contentStream.Dispose();

                #endregion

                // Из-за AllowSynchronousContinuations частично начнёт отправку текущим потоком(!).
                if (!_sendChannel.Writer.TryWrite(new SendJob(header, headerSize, mem, messageType)))
                {
                    mem.Dispose();
                }
            }
        }

        private void Execute(object rawResult, ActionContext actionContext)
        {
            if (rawResult is IActionResult actionResult)
            {
                actionResult.ExecuteResult(actionContext);
            }
            else
            {
                actionContext.StatusCode = StatusCode.Ok;
                actionContext.Request.ActionToInvoke.SerializeObject(actionContext.ResponseStream, rawResult);
                actionContext.ProducesEncoding = actionContext.Request.ActionToInvoke.ProducesEncoding;
            }
        }

        /// <summary>
        /// Принимает заказы на отправку и отправляет в сокет. Запускается из конструктора. Не бросает исключения.
        /// </summary>
        /// <returns></returns>
        private async void SenderLoop()
        // Точка входа нового потока.
        {
            while (!IsDisposed)
            {
                // Ждём сообщение для отправки.
                if (await _sendChannel.Reader.WaitToReadAsync())
                {
                    _sendChannel.Reader.TryRead(out SendJob sendJob);

                    if (sendJob.MessageType == MessageType.Request)
                    {
                        // Должны получить ответ на этот запрос.
                        if (Interlocked.Increment(ref _reqAndRespCount) == 0)
                        // Значение было -1, значит происходит остановка и сокет уже уничтожен.
                        {
                            return;
                        }
                    }

                    // Этим стримом теперь владеет только этот поток.
                    using (MemoryPoolStream mem = sendJob.MessageStream)
                    {
                        byte[] streamBuffer = mem.DangerousGetBuffer();

                        SocketError socketError;
                        try
                        {
                            // Отправить заголовок.
                            socketError = await Socket.WebSocket.SendExAsync(streamBuffer.AsMemory(0, sendJob.HeaderSize), 
                                WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(ex);

                            // Завершить поток.
                            return;
                        }

                        if (socketError == SocketError.Success)
                        {
                            #region Отправка запроса или ответа на запрос.

                            // Отправляем сообщение по частям.
                            int offset = sendJob.HeaderSize;
                            int bytesLeft = (int)mem.Length - sendJob.HeaderSize;
                            bool endOfMessage = false;
                            while (bytesLeft > 0)
                            {
                                #region Фрагментируем отправку

                                int countToSend = WebSocketMaxFrameSize;
                                if (countToSend >= bytesLeft)
                                {
                                    countToSend = bytesLeft;
                                    endOfMessage = true;
                                }

                                try
                                {
                                    socketError = await Socket.WebSocket.SendExAsync(streamBuffer.AsMemory(offset, countToSend), 
                                        WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);
                                }
                                catch (Exception ex)
                                // Обрыв соединения.
                                {
                                    // Оповестить об обрыве.
                                    AtomicDisconnect(ex);

                                    // Завершить поток.
                                    return;
                                }

                                if (socketError == SocketError.Success)
                                {
                                    bytesLeft -= countToSend;
                                    offset += countToSend;
                                }
                                else
                                {
                                    // Оповестить об обрыве.
                                    AtomicDisconnect(socketError.ToException());

                                    // Завершить поток.
                                    return;
                                }
                                #endregion
                            }
                            #endregion
                        }
                        else
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(socketError.ToException());

                            // Завершить поток.
                            return;
                        }
                    }

                    if (sendJob.MessageType == MessageType.Response)
                    {
                        // Ответ успешно отправлен.
                        if (Interlocked.Decrement(ref _reqAndRespCount) == -1)
                        // Был запрос на остановку.
                        {
                            SetCompleted();
                            return;
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

        private void SetCompleted()
        {
            _connectedTcs.TrySetResult(0);
        }

        /// <summary>
        /// Потокобезопасно освобождает ресурсы соединения. Вызывается при обрыве соединения.
        /// </summary>
        /// <param name="possibleReason">Возможная причина обрыва соединения.</param>
        private protected void AtomicDisconnect(Exception possibleReason)
        {
            // Захватить эксклюзивный доступ к сокету.
            if(Socket.TryOwn())
            // Только один поток может зайти сюда (за всю жизнь экземпляра).
            {
                // Это настоящая причина обрыва соединения.
                Exception disconnectReason = possibleReason;

                // Передать исключение всем ожидающим потокам.
                Socket.PendingRequests.PropagateExceptionAndLockup(disconnectReason);

                // Закрыть соединение.
                Socket.Dispose();

                // Синхронизироваться с подписчиками на событие Disconnected.
                lock (_disconnectEventObj)
                {
                    // Запомнить истинную причину обрыва.
                    _disconnectReason = disconnectReason;

                    // Теперь флаг.
                    _isConnected = false;

                    // Сообщить об обрыве.
                    _Disconnected?.Invoke(this, new SocketDisconnectedEventArgs(disconnectReason));

                    // Теперь можно безопасно убрать подписчиков.
                    _Disconnected = null;
                }

                // Установить Task Completed.
                // Вызывать нужно после события Disconnected.
                SetCompleted();
            }
        }

        /// <summary>
        /// Формирует сообщение ошибки из фрейма веб-сокета информирующем о закрытии соединения.
        /// </summary>
        private string GetMessageFromCloseFrame()
        {
            var webSocket = Socket.WebSocket;

            string exceptionMessage = null;
            if (webSocket.CloseStatus != null)
            {
                exceptionMessage = $"CloseStatus: {webSocket.CloseStatus.ToString()}";

                if (!string.IsNullOrEmpty(webSocket.CloseStatusDescription))
                {
                    exceptionMessage += $", Description: \"{webSocket.CloseStatusDescription}\"";
                }
            }
            else if (!string.IsNullOrEmpty(webSocket.CloseStatusDescription))
            {
                exceptionMessage = $"Description: \"{webSocket.CloseStatusDescription}\"";
            }

            if (exceptionMessage == null)
                exceptionMessage = "Удалённая сторона закрыла соединение без объяснения причины.";

            return exceptionMessage;
        }

        /// <summary>
        /// Вызывает запрошенный метод контроллера и возвращает результат.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        private async ValueTask<object> InvokeControllerAsync(RequestMessageDto receivedRequest)
        {
            // Находим контроллер.
            Type controllerType = FindRequestedController(receivedRequest, out string controllerName, out string actionName);
            if (controllerType != null)
            {
                // Ищем делегат запрашиваемой функции.
                if (_controllerActions.TryGetValue(controllerType, actionName, out ControllerAction action))
                {
                    // Контекст запроса запоминает запрашиваемый метод.
                    receivedRequest.RequestContext.ActionToInvoke = action;

                    // Проверить доступ к функции.
                    InvokeMethodPermissionCheck(action.TargetMethod, controllerType);

                    // Блок IoC выполнит Dispose всем созданным экземплярам.
                    using (IServiceScope scope = ServiceProvider.CreateScope())
                    {
                        // Активируем контроллер через IoC.
                        using (var controller = (Controller)scope.ServiceProvider.GetRequiredService(controllerType))
                        {
                            // Подготавливаем контроллер.
                            BeforeInvokeController?.Invoke(this, controller);

                            // Мапим и десериализуем аргументы по их именам.
                            //object[] args = DeserializeParameters(action.TargetMethod.GetParameters(), receivedRequest);

                            // Мапим и десериализуем аргументы по их порядку.
                            object[] args = DeserializeArguments(action.TargetMethod.GetParameters(), receivedRequest);

                            // Вызов метода контроллера.
                            object controllerResult = action.TargetMethod.InvokeFast(controller, args);

                            if (controllerResult != null)
                            {
                                // Извлекает результат из Task'а.
                                var controllerResultTask = DynamicAwaiter.WaitAsync(controllerResult);
                                if (controllerResultTask.IsCompletedSuccessfully)
                                    controllerResult = controllerResultTask.GetAwaiter().GetResult();
                                else
                                    controllerResult = await controllerResultTask;
                            }

                            // Результат успешно получен без исключения.
                            return controllerResult;
                        }
                    }
                }
                else
                    throw new BadRequestException($"Unable to find requested action \"{receivedRequest.ActionName}\".", StatusCode.ActionNotFound);
            }
            else
                throw new BadRequestException($"Unable to find requested controller \"{controllerName}\"", StatusCode.ActionNotFound);
        }

        /// <summary>
        /// Возвращает инкапсулированный в <see cref="Task"/> тип результата функции.
        /// </summary>
        private static Type GetActionReturnType(MethodInfo method)
        {
            // Если возвращаемый тип функции — Task.
            if (method.IsAsyncMethod())
            {
                // Если у задачи есть результат.
                if (method.ReturnType.IsGenericType)
                {
                    // Тип результата задачи.
                    Type resultType = method.ReturnType.GenericTypeArguments[0];
                    return resultType;
                }
                else
                {
                    // Возвращаемый тип Task(без результата).
                    return typeof(void);
                }
            }
            else
            // Была вызвана синхронная функция.
            {
                return method.ReturnType;
            }
        }

        /// <summary>
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        protected virtual void InvokeMethodPermissionCheck(MethodInfo method, Type controllerType) { }
        //protected virtual void BeforeInvokePrepareController(Controller controller) { }

        /// <summary>
        /// Пытается найти запрашиваемый пользователем контроллер.
        /// </summary>
        private Type FindRequestedController(RequestMessageDto request, out string controllerName, out string actionName)
        {
            int index = request.ActionName.IndexOf('/');
            if (index == -1)
            {
                controllerName = "Home";
                actionName = request.ActionName;
            }
            else
            {
                controllerName = request.ActionName.Substring(0, index);
                actionName = request.ActionName.Substring(index + 1);
            }

            //controllerName += "Controller";

            // Ищем контроллер в кэше.
            _controllers.TryGetValue(controllerName, out Type controllerType);

            return controllerType;
        }

        ///// <summary>
        ///// Производит маппинг аргументов запроса в соответствии с делегатом.
        ///// </summary>
        ///// <param name="method">Метод который будем вызывать.</param>
        //private object[] DeserializeParameters(ParameterInfo[] targetArguments, RequestMessage request)
        //{
        //    object[] args = new object[targetArguments.Length];

        //    for (int i = 0; i < targetArguments.Length; i++)
        //    {
        //        ParameterInfo p = targetArguments[i];
        //        var arg = request.Args.FirstOrDefault(x => x.ParameterName.Equals(p.Name, StringComparison.InvariantCultureIgnoreCase));
        //        if (arg == null)
        //            throw new BadRequestException($"Argument \"{p.Name}\" missing.");

        //        args[i] = arg.Value.ToObject(p.ParameterType);
        //    }
        //    return args;
        //}

        /// <summary>
        /// Производит маппинг аргументов по их порядку.
        /// </summary>
        /// <param name="method">Метод который будем вызывать.</param>
        private object[] DeserializeArguments(ParameterInfo[] targetArguments, RequestMessageDto request)
        {
            if (request.Args.Length == targetArguments.Length)
            {
                object[] args = new object[targetArguments.Length];
                for (int i = 0; i < targetArguments.Length; i++)
                {
                    ParameterInfo p = targetArguments[i];
                    var arg = request.Args[i];
                    args[i] = arg.ToObject(p.ParameterType);
                }
                return args;
            }
            throw new BadRequestException("Argument count mismatch.");
        }

        /// <summary>
        /// В новом потоке выполняет запрос клиента и отправляет ему результат или ошибку.
        /// </summary>
        private void StartProcessRequest(RequestMessageDto receivedRequest)
        {
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                var tuple = ((RequestMessageDto receivedRequest, Context context))state;
                tuple.context.StartProcessRequestThread(tuple.receivedRequest);

            }, state: (receivedRequest, this)); // Без замыкания.
        }

        private async void StartProcessRequestThread(RequestMessageDto receivedRequest)
        // Новый поток из пула потоков.
        {
            // Увеличить счетчик запросов.
            if (Interlocked.Increment(ref _reqAndRespCount) > 0)
            {
                // Не бросает исключения.
                // Выполнить запрос и создать сообщение с результатом.
                ValueTask<Message> responseToSendTask = GetResponseAsync(receivedRequest);
                Message responseToSend;
                if (responseToSendTask.IsCompletedSuccessfully)
                {
                    responseToSend = responseToSendTask.GetAwaiter().GetResult();
                }
                else
                {
                    responseToSend = await responseToSendTask;
                }

                // Не бросает исключения.
                // Сериализовать и отправить результат.
                QueueSendMessage(responseToSend, MessageType.Response);
            }
            else
            // Значение было -1, значит происходит остановка. Выполнять запрос не нужно.
            {
                return;
            }
        }

        /// <summary>
        /// Выполняет запрос клиента и инкапсулирует результат в <see cref="Response"/>.
        /// Не бросает исключения.
        /// </summary>
        private async ValueTask<Message> GetResponseAsync(RequestMessageDto receivedRequest)
        {
            // Результат контроллера. Может быть Task.
            object rawResult;
            try
            {
                // Находит и выполняет запрашиваемую функцию.
                ValueTask<object> rawResultTask = InvokeControllerAsync(receivedRequest);

                if(rawResultTask.IsCompletedSuccessfully)
                    rawResult = rawResultTask.GetAwaiter().GetResult();
                else
                    rawResult = await rawResultTask;
            }
            catch (BadRequestException ex)
            {
                // Вернуть результат с ошибкой.
                return Message.FromResult(receivedRequest, new BadRequestResult(ex.Message));
            }
            catch (Exception ex)
            // Злая ошибка обработки запроса. Аналогично ошибке 500.
            {
                // Прервать отладку.
                //DebugOnly.Break();

                Debug.WriteLine(ex);

                // Вернуть результат с ошибкой.
                return Message.FromResult(receivedRequest, new InternalErrorResult("Internal Server Error"));
            }

            // Запрашиваемая функция выполнена успешно.
            // Подготовить возвращаемый результат.
            return Message.FromResult(receivedRequest, rawResult);
        }

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
        /// <exception cref="StopRequiredException"/>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfStopRequired()
        {
            if (!_stopRequired)
                return;

            throw new StopRequiredException();
        }

        /// <summary>
        /// Вызывает Dispose распространяя исключение <see cref="StopRequiredException"/>.
        /// Потокобезопасно.
        /// </summary>
        internal void StopAndDispose()
        {
            Dispose(new StopRequiredException());
        }

        /// <summary>
        /// Потокобезопасно закрывает соединение и освобождает все ресурсы.
        /// </summary>
        public void Dispose()
        {
            DisposeManaged();
        }

        protected virtual void DisposeManaged()
        {
            Dispose(new ObjectDisposedException(GetType().FullName));
        }

        private void Dispose(Exception propagateException)
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                // Лучше выполнить в первую очередь.
                _sendChannel.Writer.TryComplete();

                // Оповестить об обрыве.
                AtomicDisconnect(propagateException);

                ServiceProvider.Dispose();
            }
        }
    }
}
