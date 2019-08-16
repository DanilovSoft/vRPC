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

namespace vRPC
{
    /// <summary>
    /// Контекст соединения Web-Сокета. Владеет соединением.
    /// </summary>
    public abstract class Context : IDisposable
    {
        /// <summary>
        /// Максимальный размер фрейма который может передавать протокол.
        /// </summary>
        private const int WebSocketMaxFrameSize = 4096;
        private const string ProtocolHeaderErrorMessage = "Произошла ошибка десериализации заголовка от удалённой стороны.";
        /// <summary>
        /// Содержит имена методов прокси интерфейса без постфикса Async.
        /// </summary>
        private static readonly Dictionary<MethodInfo, string> _proxyMethodName = new Dictionary<MethodInfo, string>();
        /// <summary>
        /// Объект синхронизации для создания прокси из интерфейсов.
        /// </summary>
        private readonly object _proxyObj = new object();
        /// <summary>
        /// Содержит прокси созданные из интерфейсов.
        /// </summary>
        private readonly Dictionary<Type, object> _proxies = new Dictionary<Type, object>();
        /// <summary>
        /// Потокобезопасный словарь используемый только для чтения.
        /// Хранит все доступные контроллеры. Не учитывает регистр.
        /// </summary>
        private readonly Dictionary<string, Type> _controllers;
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly ControllerActionsDictionary _controllerActions;
        private readonly TaskCompletionSource<int> _tcs = new TaskCompletionSource<int>();
        private ServiceProvider _serviceProvider;
        public ServiceProvider ServiceProvider => _serviceProvider;
        private protected volatile SocketWrapper _socket;
        /// <summary>
        /// => <see langword="volatile"/> _socket.
        /// </summary>
        private protected SocketWrapper Socket { get => _socket; set => _socket = value; }
        public EndPoint LocalEndPoint => _socket.WebSocket.LocalEndPoint;
        public EndPoint RemoteEndPoint => _socket.WebSocket.RemoteEndPoint;
        /// <summary>
        /// Отправка сообщения <see cref="Message"/> должна выполняться только с захватом этой блокировки.
        /// </summary>
        private readonly Channel<SendJob> _sendChannel;
        private int _disposed;
        private bool IsDisposed => Volatile.Read(ref _disposed) == 1;
        private volatile bool _stopRequired;
        /// <summary>
        /// Возвращает <see cref="Task"/> который завершается когда сервис полностью 
        /// остановлен и больше не будет обрабатывать запросы.
        /// </summary>
        public Task Completion => _tcs.Task;
        /// <summary>
        /// Количество запросов для обработки + количество ответов для отправки.
        /// Для отслеживания грациозной остановки.
        /// </summary>
        private int _reqAndRespCount;

        // static ctor.
        static Context()
        {
            // Прогрев сериализатора.
            ProtoBuf.Serializer.PrepareSerializer<Header>();
        }

        // ctor.
        /// <summary>
        /// Конструктор клиента.
        /// </summary>
        /// <param name="controllersAssembly">Сборка в которой будет осеществляться поиск контроллеров.</param>
        internal Context(Assembly controllersAssembly) : this()
        {
            // Сборка с контроллерами не должна быть текущей сборкой.
            Debug.Assert(controllersAssembly != Assembly.GetExecutingAssembly());

            // Словарь с найденными контроллерами в вызывающей сборке.
            _controllers = GlobalVars.FindAllControllers(controllersAssembly);

            _controllerActions = new ControllerActionsDictionary(_controllers);

            // Запустить диспетчер отправки сообщений.
            _sendChannel = StartChannelSender();
        }

        // ctor.
        /// <summary>
        /// Конструктор сервера — Когда подключается клиент.
        /// </summary>
        /// <param name="ioc">Контейнер Listener'а.</param>
        internal Context(MyWebSocket clientConnection, ServiceProvider serviceProvider, Dictionary<string, Type> controllers) : this()
        {
            // У сервера сокет всегда подключен и переподключаться не может.
            Socket = new SocketWrapper(clientConnection);

            // IoC готов к работе.
            _serviceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _controllers = controllers;

            _controllerActions = new ControllerActionsDictionary(controllers);

            // Запустить диспетчер отправки сообщений.
            _sendChannel = StartChannelSender();
        }

        // ctor.
        private Context()
        {
            
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
        /// Вызывается единожды клиентским контектом.
        /// </summary>
        private protected void ConfigureIoC(ServiceCollection ioc)
        {
            // Добавим в IoC все контроллеры сборки.
            foreach (Type controllerType in _controllers.Values)
                ioc.AddScoped(controllerType);

            ServiceProvider serviceProvider = ioc.BuildServiceProvider();

            // IoC готов к работе.
            if(Interlocked.CompareExchange(ref _serviceProvider, serviceProvider, null) != null)
            {
                // Нельзя устанавливать IoC повторно.
                serviceProvider.Dispose();
            }
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// </summary>
        public T GetProxy<T>()
        {
            var attrib = typeof(T).GetCustomAttribute<ControllerContractAttribute>(inherit: false);
            if (attrib == null)
                throw new ArgumentNullException("controllerName", $"Укажите имя контроллера или пометьте интерфейс атрибутом \"{nameof(ControllerContractAttribute)}\"");

            return GetProxy<T>(attrib.ControllerName);
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// </summary>
        /// <param name="controllerName">Имя контроллера на удалённой стороне к которому применяется текущий интерфейс <see cref="{T}"/>.</param>
        public T GetProxy<T>(string controllerName)
        {
            Type interfaceType = typeof(T);
            lock (_proxyObj)
            {
                if (_proxies.TryGetValue(interfaceType, out object proxy))
                {
                    return (T)proxy;
                }
                else
                {
                    T proxyT = TypeProxy.Create<T, InterfaceProxy>((this, controllerName));
                    _proxies.Add(interfaceType, proxyT);
                    return proxyT;
                }
            }
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        internal object OnProxyCall(MethodInfo targetMethod, object[] args, string controllerName)
        {
            ThrowIfDisposed();

            #region CreateArgs()
            Arg[] CreateArgs()
            {
                ParameterInfo[] par = targetMethod.GetParameters();
                Arg[] retArgs = new Arg[par.Length];

                for (int i = 0; i < par.Length; i++)
                {
                    ParameterInfo p = par[i];
                    retArgs[i] = new Arg(p.Name, args[i]);
                }
                return retArgs;
            }
            #endregion

            // Без постфикса Async.
            string remoteMethodName = GetProxyMethodName(targetMethod);

            // Подготавливаем запрос для отправки.
            var requestToSend = Message.CreateRequest($"{controllerName}/{remoteMethodName}", CreateArgs());

            // Тип результата инкапсулированный в Task<T>.
            Type resultType = GetActionReturnType(targetMethod);

            // Отправляет запрос и получает результат от удалённой стороны.
            Task<object> taskObject = ExecuteRequestAsync(requestToSend, resultType, Socket);

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
                object rawResult = taskObject.GetAwaiter().GetResult();
                return rawResult;
            }
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
        /// Создает подключение или возвращает уже подготовленное соединение.
        /// </summary>
        /// <returns></returns>
        private protected abstract Task<SocketWrapper> GetOrCreateConnectionAsync();

        /// <summary>
        /// Происходит атомарно для экземпляра подключения.
        /// </summary>
        /// <param name="socketQueue">Соединение в котором произошло отключение.</param>
        private protected abstract void OnAtomicDisconnect(SocketWrapper socketQueue, Exception exception);

        /// <summary>
        /// Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// <param name="returnType">Тип в который будет десериализован результат запроса.</param>
        private protected async Task<object> ExecuteRequestAsync(Message requestToSend, Type returnType, SocketWrapper socketQueue)
        {
            if (_stopRequired)
            {
                // Не позволять начинать новый запрос если происходит остановка.
                throw new StopRequiredException();
            }

            // У клиента соединение может быть ещё не установлено.
            if (socketQueue == null)
            {
                // Никогда не вызывается серверным контекстом.
                socketQueue = await GetOrCreateConnectionAsync().ConfigureAwait(false);
            }

            // Добавить запрос в словарь для дальнейшей связки с ответом.
            TaskCompletionSource tcs = socketQueue.RequestCollection.AddRequest(returnType, requestToSend.ActionName, out short uid);

            // Назначить запросу уникальный идентификатор.
            requestToSend.Uid = uid;

            // Планируем отправку запроса.
            QueueSendMessage(socketQueue, requestToSend, MessageType.Request);

            // Ожидаем результат от потока поторый читает из сокета.
            object rawResult = await tcs;

            // Успешно получили результат без исключений.
            return rawResult;
        }

        /// <summary>
        /// Запускает бесконечный цикл, в фоновом потоке, считывающий из сокета запросы и ответы.
        /// </summary>
        private protected void StartReceivingLoop(SocketWrapper socketQueue_)
        {
            ThreadPool.UnsafeQueueUserWorkItem(async state =>
            {
                var socketQueue = (SocketWrapper)state;

                // Не бросает исключения.
                await ReceivingLoopThreadAsync(socketQueue);
                
            }, state: socketQueue_); // Без замыкания.
        }

        private async Task ReceivingLoopThreadAsync(SocketWrapper socketQueue)
        {
            // Бесконечно обрабатываем сообщения сокета.
            while (!IsDisposed)
            {
                int headerLength = 0;

                // Арендуем память на фрейм веб-сокета.
                using (var mem = new MemoryPoolStream(128))
                {
                    #region Читаем запрос или ответ на запрос.

                    Header header = null;
                    ValueWebSocketReceiveExResult webSocketMessage;

                    // Арендуем максимум памяти на один фрейм веб-сокета.
                    using (var frameMem = new MemoryPoolStream(WebSocketMaxFrameSize))
                    {
                        byte[] pooledBuffer = frameMem.DangerousGetBuffer();
                        var segment = new ArraySegment<byte>(pooledBuffer);

                        // Пока не EndOfMessage
                        do
                        {
                            #region Пока не EndOfMessage записывать в буфер памяти

                            #region Читаем фрейм веб-сокета.

                            try
                            {
                                // Читаем фрейм веб-сокета.
                                webSocketMessage = await socketQueue.WebSocket.ReceiveExAsync(segment, CancellationToken.None);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDisconnect(socketQueue, ex);

                                // Завершить поток.
                                return;
                            }
                            #endregion

                            if (webSocketMessage.ErrorCode == SocketError.Success)
                            {
                                #region Проверка на Close.

                                // Другая сторона закрыла соединение.
                                if (webSocketMessage.MessageType == WebSocketMessageType.Close)
                                {
                                    // Сформировать причину закрытия соединения.
                                    string exceptionMessage = GetMessageFromCloseFrame(webSocketMessage, socketQueue.WebSocket);

                                    // Сообщить потокам что удалённая сторона выполнила закрытие соединения.
                                    var socketClosedException = new SocketClosedException(exceptionMessage);

                                    // Оповестить об обрыве.
                                    AtomicDisconnect(socketQueue, socketClosedException);

                                    // Завершить поток.
                                    return;
                                }
                                #endregion

                                // Копирование фрейма в MemoryStream.
                                mem.Write(pooledBuffer, 0, webSocketMessage.Count);

                                if (header == null && Header.TryReadLengthPrefix(mem, out int length) && mem.Length >= (length + 4))
                                // Пора десериализовать заголовок.
                                {
                                    #region Десериализуем фрейм веб-сокета в заголовок протокола.

                                    mem.Position = 0;
                                    try
                                    {
                                        header = Header.DeserializeWithLengthPrefix(mem);
                                    }
                                    catch (Exception headerException)
                                    // Не удалось десериализовать заголовок.
                                    {
                                        #region Отправка Close

                                        var protocolErrorException = new ProtocolErrorException(ProtocolHeaderErrorMessage, headerException);

                                        // Сообщить потокам что обрыв произошел по вине удалённой стороны.
                                        socketQueue.RequestCollection.OnDisconnect(protocolErrorException);

                                        try
                                        {
                                            // Отключаемся от сокета с небольшим таймаутом.
                                            using (var cts = new CancellationTokenSource(3000))
                                                await socketQueue.WebSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "Ошибка десериализации заголовка.", cts.Token);
                                        }
                                        catch (Exception ex)
                                        // Злой обрыв соединения.
                                        {
                                            // Оповестить об обрыве.
                                            AtomicDisconnect(socketQueue, ex);

                                            // Завершить поток.
                                            return;
                                        }

                                        // Оповестить об обрыве.
                                        AtomicDisconnect(socketQueue, protocolErrorException);

                                        // Завершить поток.
                                        return;
                                        #endregion
                                    }

                                    // Размер заголовка определён.
                                    headerLength = (int)mem.Position;

                                    // Если есть еще фреймы веб-сокета.
                                    if (!webSocketMessage.EndOfMessage)
                                    {
                                        // Возвращаем позицию стрима в конец для следующей записи.
                                        mem.Position = mem.Length;

                                        // Увеличим стрим до размера всего сообщения.
                                        mem.Capacity = (header.ContentLength + headerLength);
                                    }

                                    #endregion
                                }
                            }
                            else
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDisconnect(socketQueue, new SocketException((int)webSocketMessage.ErrorCode));

                                // Завершить поток.
                                return;
                            }

                            #endregion
                        } while (!webSocketMessage.EndOfMessage);
                    }
                    
                    #endregion

                    if (header != null)
                    {
                        #region Обработка Payload

                        // Установить курсор после заголовка.
                        mem.Position = headerLength;

                        if (header.StatusCode == StatusCode.Request)
                        // Получен запрос.
                        {
                            #region Выполнение запроса

                            #region Десериализация запроса

                            RequestMessage receivedRequest;
                            try
                            {
                                receivedRequest = ExtensionMethods.DeserializeJson<RequestMessage>(mem);
                            }
                            catch (Exception ex)
                            // Ошибка десериализации запроса.
                            {
                                #region Игнорируем запрос

                                // Подготовить ответ с ошибкой.
                                var errorResponse = Message.FromResult(header.Uid, new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{ex.Message}\"."));

                                // Передать на отправку результат с ошибкой.
                                QueueSendMessage(socketQueue, errorResponse, MessageType.Response);

                                // Вернуться к чтению из сокета.
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
                            StartProcessRequestAsync(socketQueue, receivedRequest);
                            #endregion

                            #endregion
                        }
                        else
                        // Получен ответ на запрос.
                        {
                            #region Передача другому потоку ответа на запрос

                            // Удалить запрос из словаря.
                            if (socketQueue.RequestCollection.TryTake(header.Uid, out TaskCompletionSource tcs))
                            // Передать ответ ожидающему потоку.
                            {
                                #region Передать ответ ожидающему потоку

                                if (header.StatusCode == StatusCode.Ok)
                                // Запрос на удалённой стороне был выполнен успешно.
                                {
                                    #region Передать успешный результат

                                    if (tcs.ResultType != typeof(void))
                                    {
                                        // Десериализатор в соответствии с ContentEncoding.
                                        var deserializer = header.GetDeserializer();

                                        bool deserialized;
                                        object rawResult = null;
                                        try
                                        {
                                            rawResult = deserializer(mem, tcs.ResultType);
                                            deserialized = true;
                                        }
                                        catch (Exception deserializationException)
                                        {
                                            var protocolErrorException = new ProtocolErrorException($"Произошла ошибка десериализации " +
                                                $"результата запроса типа \"{tcs.ResultType.FullName}\"", deserializationException);

                                            // Сообщить ожидающему потоку что произошла ошибка при разборе ответа удаленной стороны.
                                            tcs.TrySetException(protocolErrorException);

                                            deserialized = false;
                                        }

                                        if (deserialized)
                                        {
                                            // Передать результат ожидающему потоку.
                                            tcs.TrySetResult(rawResult);
                                        }
                                    }
                                    else
                                    // void.
                                    {
                                        tcs.TrySetResult(null);
                                    }
                                    #endregion
                                }
                                else
                                // Сервер прислал код ошибки.
                                {
                                    // Телом ответа в этом случае будет строка.
                                    string errorMessage = mem.ReadAsString();

                                    // Сообщить ожидающему потоку что удаленная сторона вернула ошибку в результате выполнения запроса.
                                    tcs.TrySetException(new BadRequestException(errorMessage, header.StatusCode));
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
                        socketQueue.RequestCollection.OnDisconnect(protocolErrorException);

                        try
                        {
                            // Отключаемся от сокета с небольшим таймаутом.
                            using (var cts = new CancellationTokenSource(3000))
                                await socketQueue.WebSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "Непредвиденное завершение потока данных.", cts.Token);
                        }
                        catch (Exception ex)
                        // Злой обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(socketQueue, ex);

                            // Завершить поток.
                            return;
                        }

                        // Оповестить об обрыве.
                        AtomicDisconnect(socketQueue, protocolErrorException);

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
        private void QueueSendMessage(SocketWrapper socketQueue, Message messageToSend, MessageType messageType)
        {
            // На текущем этапе сокет может быть уже уничтожен другим потоком
            // В результате чего в текущем потоке случилась ошибка но отправлять её не нужно.
            if (socketQueue.IsDisposed)
                return;

            MemoryPoolStream mem;

            // Сериализуем контент.
            #region Сериализуем сообщение

            using (var contentStream = new MemoryPoolStream())
            {
                ActionContext actionContext = null;

                // Записать в стрим запрос или результат запроса.
                if (messageToSend.IsRequest)
                {
                    var request = new RequestMessage
                    {
                        ActionName = messageToSend.ActionName,
                        Args = messageToSend.Args,
                    };
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
                var header = new Header(messageToSend.Uid, actionContext?.StatusCode ?? StatusCode.Request)
                {
                    ContentLength = contentLength,
                };

                if (actionContext != null)
                {
                    // Записать в заголовок формат контента.
                    header.ContentEncoding = actionContext.ProducesEncoding;
                }

                mem = new MemoryPoolStream(contentLength);

                // Записать заголовок в самое начало с размер-префиксом.
                header.SerializeWithLengthPrefix(mem);

                byte[] buffer = contentStream.DangerousGetBuffer();
                mem.Write(buffer, 0, contentLength);
            }

            #endregion

            // Из-за AllowSynchronousContinuations частично начнёт отправку текущим потоком(!).
            if (!_sendChannel.Writer.TryWrite(new SendJob(socketQueue, mem, messageType)))
            {
                mem.Dispose();
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
        /// Принимает заказы на отправку и отправляет в сокет. Запускается из конструктора.
        /// </summary>
        private Channel<SendJob> StartChannelSender()
        {
            var sendChannel = Channel.CreateUnbounded<SendJob>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true, // Внимательнее с этим параметром!
                SingleReader = true,
                SingleWriter = false,
            });

            ThreadPool.UnsafeQueueUserWorkItem(async state =>
            {
                var channel = (Channel<SendJob>)state;
                
                // Не бросает исключения.
                await ChannelSenderThreadAsync(channel);
                    
            }, state: sendChannel);

            return sendChannel;
        }

        // Не бросает исключения.
        private async Task ChannelSenderThreadAsync(Channel<SendJob> channel)
        {
            while (!IsDisposed)
            {
                if (await channel.Reader.WaitToReadAsync())
                {
                    channel.Reader.TryRead(out SendJob sendJob);

                    if (sendJob.MessageType == MessageType.Request)
                    {
                        // Должны получить ответ на этот запрос.
                        if (Interlocked.Increment(ref _reqAndRespCount) == 0)
                        // Значение было -1, значит происходит остановка и сокет уже уничтожен.
                        {
                            return;
                        }
                    }

                    #region Отправка запроса или ответа на запрос.

                    // Этим стримом теперь владеет только этот поток.
                    using (MemoryPoolStream mem = sendJob.MemoryPoolStream)
                    {
                        // Отправляем сообщение по частям.
                        int offset = 0;
                        int bytesLeft = (int)mem.Length;
                        bool endOfMessage = false;
                        do
                        {
                            #region Фрагментируем отправку

                            int countToSend = WebSocketMaxFrameSize;
                            if (countToSend >= bytesLeft)
                            {
                                countToSend = bytesLeft;
                                endOfMessage = true;
                            }

                            byte[] streamBuffer = mem.DangerousGetBuffer();

                            var segment = new ArraySegment<byte>(streamBuffer, offset, countToSend);

                            try
                            {
                                await sendJob.SocketQueue.WebSocket.SendAsync(segment, WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDisconnect(sendJob.SocketQueue, ex);

                                // Завершить поток.
                                return;
                            }

                            bytesLeft -= countToSend;
                            offset += countToSend;

                            #endregion

                        } while (!endOfMessage);
                    }

                    #endregion

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
            _tcs.TrySetResult(0);
        }

        /// <summary>
        /// Потокобезопасно освобождает ресурсы соединения. Вызывается при обрыве соединения.
        /// </summary>
        /// <param name="socketQueue">Экземпляр в котором произошел обрыв.</param>
        /// <param name="exception">Возможная причина обрыва соединения.</param>
        private protected void AtomicDisconnect(SocketWrapper socketQueue, Exception exception)
        {
            // Захватить эксклюзивный доступ к сокету.
            if(socketQueue.TryOwn())
            // Только один поток может зайти сюда.
            {
                // Передать исключение всем ожидающим потокам.
                socketQueue.RequestCollection.OnDisconnect(exception);

                // Закрыть соединение.
                socketQueue.Dispose();

                //// Отменить все операции контроллеров связанных с текущим соединением.
                //_cts.Cancel();

                // Сообщить наследникам об обрыве.
                OnAtomicDisconnect(socketQueue, exception);
            }
        }

        /// <summary>
        /// Формирует сообщение ошибки из фрейма веб-сокета информирующем о закрытии соединения.
        /// </summary>
        private string GetMessageFromCloseFrame(ValueWebSocketReceiveExResult webSocketMessage, DanilovSoft.WebSocket.WebSocket webSocket)
        {
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
        private async ValueTask<object> InvokeControllerAsync(RequestMessage receivedRequest)
        {
            // Находим контроллер.
            Type controllerType = FindRequestedController(receivedRequest, out string controllerName, out string actionName);
            if(controllerType == null)
                throw new BadRequestException($"Unable to find requested controller \"{controllerName}\"", StatusCode.ActionNotFound);

            // Ищем делегат запрашиваемой функции.
            if (!_controllerActions.TryGetValue(controllerType, actionName, out ControllerAction action))
                throw new BadRequestException($"Unable to find requested action \"{receivedRequest.ActionName}\".", StatusCode.ActionNotFound);

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
                    BeforeInvokePrepareController(controller);

                    // Мапим и десериализуем аргументы по их именам.
                    //object[] args = DeserializeParameters(action.TargetMethod.GetParameters(), receivedRequest);

                    // Мапим и десериализуем аргументы по их порядку.
                    object[] args = DeserializeArguments(action.TargetMethod.GetParameters(), receivedRequest);

                    // Вызов делегата.
                    object controllerResult = action.TargetMethod.InvokeFast(controller, args);

                    if (controllerResult != null)
                    {
                        // Извлекает результат из Task'а.
                        controllerResult = await DynamicAwaiter.WaitAsync(controllerResult);
                    }

                    // Результат успешно получен без исключения.
                    return controllerResult;
                }
            }
        }

        /// <summary>
        /// Возвращает инкапсулированный в Task тип результата функции.
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
        protected abstract void InvokeMethodPermissionCheck(MethodInfo method, Type controllerType);
        protected abstract void BeforeInvokePrepareController(Controller controller);

        /// <summary>
        /// Пытается найти запрашиваемый пользователем контроллер.
        /// </summary>
        private Type FindRequestedController(RequestMessage request, out string controllerName, out string actionName)
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

        /// <summary>
        /// Производит маппинг аргументов запроса в соответствии с делегатом.
        /// </summary>
        /// <param name="method">Метод который будем вызывать.</param>
        private object[] DeserializeParameters(ParameterInfo[] targetArguments, RequestMessage request)
        {
            object[] args = new object[targetArguments.Length];

            for (int i = 0; i < targetArguments.Length; i++)
            {
                ParameterInfo p = targetArguments[i];
                var arg = request.Args.FirstOrDefault(x => x.ParameterName.Equals(p.Name, StringComparison.InvariantCultureIgnoreCase));
                if (arg == null)
                    throw new BadRequestException($"Argument \"{p.Name}\" missing.");

                args[i] = arg.Value.ToObject(p.ParameterType);
            }
            return args;
        }

        /// <summary>
        /// Производит маппинг аргументов по их порядку.
        /// </summary>
        /// <param name="method">Метод который будем вызывать.</param>
        private object[] DeserializeArguments(ParameterInfo[] targetArguments, RequestMessage request)
        {
            if (request.Args.Length != targetArguments.Length)
                throw new BadRequestException("Argument count mismatch.");

            object[] args = new object[targetArguments.Length];
            for (int i = 0; i < targetArguments.Length; i++)
            {
                ParameterInfo p = targetArguments[i];
                var arg = request.Args[i];
                args[i] = arg.Value.ToObject(p.ParameterType);
            }
            return args;
        }

        /// <summary>
        /// В новом потоке выполняет запрос клиента и отправляет ему результат или ошибку.
        /// </summary>
        private void StartProcessRequestAsync(SocketWrapper socketQueue_, RequestMessage receivedRequest_)
        {
            ThreadPool.UnsafeQueueUserWorkItem(async state =>
            {
                var tuple = ((SocketWrapper socketQueue, RequestMessage receivedRequest))state;

                // Увеличить счетчик запросов.
                if (Interlocked.Increment(ref _reqAndRespCount) > 0)
                {
                    // Не бросает исключения.
                    // Выполнить запрос и создать сообщение с результатом.
                    Message responseToSend = await GetResponseAsync(tuple.receivedRequest);

                    // Не бросает исключения.
                    // Сериализовать и отправить результат.
                    QueueSendMessage(tuple.socketQueue, responseToSend, MessageType.Response);
                }
                else
                // Значение было -1, значит происходит остановка. Выполнять запрос не нужно.
                {
                    return;
                }
            }, state: (socketQueue_, receivedRequest_)); // Без замыкания.
        }

        /// <summary>
        /// Выполняет запрос клиента и инкапсулирует результат в <see cref="Response"/>.
        /// Не бросает исключения.
        /// </summary>
        private async ValueTask<Message> GetResponseAsync(RequestMessage receivedRequest)
        {
            // Результат контроллера. Может быть Task.
            object rawResult;
            try
            {
                // Находит и выполняет запрашиваемую функцию.
                rawResult = await InvokeControllerAsync(receivedRequest);
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

        [DebuggerStepThrough]
        /// <exception cref="ObjectDisposedException"/>
        private void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
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
        /// Потокобезопасно освобождает все ресурсы.
        /// </summary>
        public virtual void Dispose()
        {
            var disposedException = new ObjectDisposedException(GetType().FullName);
            Dispose(disposedException);
        }

        private void Dispose(Exception propagateException)
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                SocketWrapper socket = Socket;
                if (socket != null)
                {
                    // Оповестить об обрыве.
                    AtomicDisconnect(socket, propagateException);
                }

                //_serviceProvider?.Dispose();
                _sendChannel.Writer.TryComplete();
            }
        }
    }
}
