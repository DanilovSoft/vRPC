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
        private readonly TaskCompletionSource<int> _tcs = new TaskCompletionSource<int>();
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
        public Task Completion => _tcs.Task;
        /// <summary>
        /// Количество запросов для обработки и количество ответов для отправки.
        /// Для отслеживания грациозной остановки сервиса.
        /// </summary>
        private int _reqAndRespCount;
        internal event EventHandler<Exception> Disconnected;
        internal event EventHandler<Controller> BeforeInvokeController;

        // static ctor.
        static Context()
        {
            // Прогрев сериализатора.
            ProtoBuf.Serializer.PrepareSerializer<Header>();
        }

        // ctor.
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ioc">Контейнер Listener'а.</param>
        internal Context(MyWebSocket clientConnection, ServiceProvider serviceProvider, Dictionary<string, Type> controllers) : this()
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

            ThreadPool.UnsafeQueueUserWorkItem(async state =>
            {
                var context = (Context)state;

                // Не бросает исключения.
                await context.ChannelSenderThreadAsync();

            }, state: this);
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
            TaskCompletionSource tcs = Socket.RequestCollection.AddRequest(returnType, requestToSend.ActionName, out ushort uid);

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
            ThreadPool.UnsafeQueueUserWorkItem(async state =>
            {
                var context = (Context)state;

                // Не бросает исключения.
                await context.ReceivingLoopThreadAsync();
                
            }, state: this); // Без замыкания.
        }

        private async Task ReceivingLoopThreadAsync()
        {
            byte[] headerBuffer = new byte[Header.HeaderMaxSize];

            // Бесконечно обрабатываем сообщения сокета.
            while (!IsDisposed)
            {
                #region Читаем хедер.

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

                Header header;
                if (webSocketMessage.ErrorCode == SocketError.Success)
                {
                    try
                    {
                        header = Header.DeserializeProtobuf(headerBuffer, 0, webSocketMessage.Count);
                    }
                    catch (Exception headerException)
                    // Не удалось десериализовать заголовок.
                    {
                        #region Отправка Close и выход

                        var protocolErrorException = new ProtocolErrorException(ProtocolHeaderErrorMessage, headerException);

                        // Сообщить потокам что обрыв произошел по вине удалённой стороны.
                        Socket.RequestCollection.OnDisconnect(protocolErrorException);

                        try
                        {
                            // Отключаемся от сокета с небольшим таймаутом.
                            using (var cts = new CancellationTokenSource(3000))
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

                using (var framesMem = new MemoryPoolStream(header.ContentLength))
                {
                    framesMem.SetLength(header.ContentLength);

                    int countLeft = header.ContentLength;
                    // Пока не EndOfMessage
                    do
                    {
                        #region Пока не EndOfMessage записывать в буфер памяти

                        #region Читаем фрейм веб-сокета.

                        int offset = 0;
                        byte[] memBuffer = framesMem.DangerousGetBuffer();
                        try
                        {
                            // Читаем фрейм веб-сокета.
                            webSocketMessage = await Socket.WebSocket.ReceiveExAsync(memBuffer.AsMemory().Slice(offset, countLeft), CancellationToken.None);
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
                            framesMem.Position += webSocketMessage.Count;
                            offset += webSocketMessage.Count;
                            countLeft -= webSocketMessage.Count;

                            #region Проверка на Close и выход.

                            // Другая сторона закрыла соединение.
                            if (webSocketMessage.MessageType == WebSocketMessageType.Close)
                            {
                                // Сформировать причину закрытия соединения.
                                string exceptionMessage = GetMessageFromCloseFrame();

                                // Сообщить потокам что удалённая сторона выполнила закрытие соединения.
                                var socketClosedException = new SocketClosedException(exceptionMessage);

                                // Оповестить об обрыве.
                                AtomicDisconnect(socketClosedException);

                                // Завершить поток.
                                return;
                            }
                            #endregion

                            //framesMem.Write(memBuffer, 0, webSocketMessage.Count);

                            //if (header == null)
                            //{
                            //    if (Header.TryGetHeaderLength(pooledBuffer, webSocketMessage.Count, out headerLength))
                            //    // Пора десериализовать заголовок.
                            //    {
                            //        #region Десериализуем заголовок протокола.

                            //        framesMem.Position = 0;
                            //        try
                            //        {
                            //            header = Header.DeserializeWithLengthPrefix(pooledBuffer, 0, headerLength);
                            //        }
                            //        catch (Exception headerException)
                            //        // Не удалось десериализовать заголовок.
                            //        {
                            //            #region Отправка Close и выход

                            //            var protocolErrorException = new ProtocolErrorException(ProtocolHeaderErrorMessage, headerException);

                            //            // Сообщить потокам что обрыв произошел по вине удалённой стороны.
                            //            _socket.RequestCollection.OnDisconnect(protocolErrorException);

                            //            try
                            //            {
                            //                // Отключаемся от сокета с небольшим таймаутом.
                            //                using (var cts = new CancellationTokenSource(3000))
                            //                    await _socket.WebSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "Ошибка десериализации заголовка.", cts.Token);
                            //            }
                            //            catch (Exception ex)
                            //            // Злой обрыв соединения.
                            //            {
                            //                // Оповестить об обрыве.
                            //                AtomicDisconnect(ex);

                            //                // Завершить поток.
                            //                return;
                            //            }

                            //            // Оповестить об обрыве.
                            //            AtomicDisconnect(protocolErrorException);

                            //            // Завершить поток.
                            //            return;
                            //            #endregion
                            //        }

                            //        // Если есть еще фреймы веб-сокета.
                            //        if (!webSocketMessage.EndOfMessage)
                            //        {
                            //            // Возвращаем позицию стрима в конец для следующей записи.
                            //            framesMem.Position = framesMem.Length;

                            //            // Увеличим стрим до размера всего сообщения.
                            //            framesMem.Capacity = (header.ContentLength + headerLength);
                            //        }
                            //        else
                            //        {
                            //            pooledBuffer = framesMem.DangerousGetBuffer();
                            //        }

                            //        #endregion
                            //    }
                            //}
                            //else
                            //{
                            // Копирование фрейма в MemoryStream.
                            //framesMem.Write(pooledBuffer, 0, webSocketMessage.Count);
                            //}
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
                        framesMem.Position = 0;

                        if (header.StatusCode == StatusCode.Request)
                        // Получен запрос.
                        {
                            #region Выполнение запроса

                            #region Десериализация запроса

                            RequestMessage receivedRequest;
                            try
                            {
                                // Десериализуем запрос.
                                receivedRequest = ExtensionMethods.DeserializeRequestJson(framesMem);
                            }
                            catch (Exception ex)
                            // Ошибка десериализации запроса.
                            {
                                #region Игнорируем запрос

                                // Подготовить ответ с ошибкой.
                                var errorResponse = Message.FromResult(header.Uid, new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{ex.Message}\"."));

                                // Передать на отправку результат с ошибкой.
                                QueueSendMessage(errorResponse, MessageType.Response);

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
                            StartProcessRequest(receivedRequest);
                            #endregion

                            #endregion
                        }
                        else
                        // Получен ответ на запрос.
                        {
                            #region Передача другому потоку ответа на запрос

                            // Удалить запрос из словаря.
                            if (Socket.RequestCollection.TryTake(header.Uid, out TaskCompletionSource tcs))
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
                                            rawResult = deserializer(framesMem, tcs.ResultType);
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
                                    string errorMessage = framesMem.ReadAsString();

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
                        Socket.RequestCollection.OnDisconnect(protocolErrorException);

                        try
                        {
                            // Отключаемся от сокета с небольшим таймаутом.
                            using (var cts = new CancellationTokenSource(3000))
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
        private async Task ChannelSenderThreadAsync()
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
                    using (MemoryPoolStream mem = sendJob.ContentStream)
                    {
                        byte[] streamBuffer = mem.DangerousGetBuffer();

                        try
                        {
                            // Отправить заголовок.
                            await Socket.WebSocket.SendAsync(streamBuffer.AsMemory(0, sendJob.HeaderSize), WebSocketMessageType.Binary, true, CancellationToken.None);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(ex);

                            // Завершить поток.
                            return;
                        }

                        #region Отправка запроса или ответа на запрос.

                        // Отправляем сообщение по частям.
                        int offset = sendJob.HeaderSize;
                        int bytesLeft = (int)mem.Length - sendJob.HeaderSize;
                        bool endOfMessage = false;
                        while(bytesLeft > 0)
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
                                await Socket.WebSocket.SendAsync(streamBuffer.AsMemory(offset, countToSend), WebSocketMessageType.Binary, endOfMessage, CancellationToken.None);
                            }
                            catch (Exception ex)
                            // Обрыв соединения.
                            {
                                // Оповестить об обрыве.
                                AtomicDisconnect(ex);

                                // Завершить поток.
                                return;
                            }

                            bytesLeft -= countToSend;
                            offset += countToSend;

                            #endregion
                        }
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
        /// <param name="exception">Возможная причина обрыва соединения.</param>
        private protected void AtomicDisconnect(Exception exception)
        {
            // Захватить эксклюзивный доступ к сокету.
            if(Socket.TryOwn())
            // Только один поток может зайти сюда.
            {
                // Передать исключение всем ожидающим потокам.
                Socket.RequestCollection.OnDisconnect(exception);

                // Закрыть соединение.
                Socket.Dispose();

                //// Отменить все операции контроллеров связанных с текущим соединением.
                //_cts.Cancel();

                // Сообщить об обрыве.
                Disconnected?.Invoke(this, exception);
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
                    BeforeInvokeController?.Invoke(this, controller);

                    // Мапим и десериализуем аргументы по их именам.
                    //object[] args = DeserializeParameters(action.TargetMethod.GetParameters(), receivedRequest);

                    // Мапим и десериализуем аргументы по их порядку.
                    object[] args = DeserializeArguments(action.TargetMethod.GetParameters(), receivedRequest);

                    // Вызов делегата.
                    object controllerResult = action.TargetMethod.InvokeFast(controller, args);

                    if (controllerResult != null)
                    {
                        // Извлекает результат из Task'а.
                        var controllerResultTask = DynamicAwaiter.WaitAsync(controllerResult);
                        if (controllerResultTask.IsCompletedSuccessfully)
                        {
                            controllerResult = controllerResultTask.GetAwaiter().GetResult();
                        }
                        else
                        {
                            controllerResult = await controllerResultTask;
                        }
                    }

                    // Результат успешно получен без исключения.
                    return controllerResult;
                }
            }
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
            if (request.Args.Length == targetArguments.Length)
            {
                object[] args = new object[targetArguments.Length];
                for (int i = 0; i < targetArguments.Length; i++)
                {
                    ParameterInfo p = targetArguments[i];
                    var arg = request.Args[i];
                    args[i] = arg.Value.ToObject(p.ParameterType);
                }
                return args;
            }
            throw new BadRequestException("Argument count mismatch.");
        }

        /// <summary>
        /// В новом потоке выполняет запрос клиента и отправляет ему результат или ошибку.
        /// </summary>
        private void StartProcessRequest(RequestMessage receivedRequest)
        {
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                var tuple = ((RequestMessage receivedRequest, Context context))state;
                tuple.context.StartProcessRequestThread(tuple.receivedRequest);

            }, state: (receivedRequest, this)); // Без замыкания.
        }

        private async void StartProcessRequestThread(RequestMessage receivedRequest)
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
        private async ValueTask<Message> GetResponseAsync(RequestMessage receivedRequest)
        {
            // Результат контроллера. Может быть Task.
            object rawResult;
            try
            {
                // Находит и выполняет запрашиваемую функцию.
                var rawResultTask = InvokeControllerAsync(receivedRequest);
                if(rawResultTask.IsCompletedSuccessfully)
                {
                    rawResult = rawResultTask.GetAwaiter().GetResult();
                }
                else
                {
                    rawResult = await rawResultTask;
                }
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
        /// Потокобезопасно освобождает все ресурсы.
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
                // Оповестить об обрыве.
                AtomicDisconnect(propagateException);

                ServiceProvider.Dispose();
                _sendChannel.Writer.TryComplete();
            }
        }
    }
}
