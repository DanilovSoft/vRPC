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
using MyWebSocket = DanilovSoft.WebSocket.ManagedWebSocket;
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
    public abstract class ManagedConnection : IDisposable
    {
        /// <summary>
        /// Максимальный размер фрейма который может передавать протокол. Сообщение может быть фрагментированно фреймами размером не больше этого значения.
        /// </summary>
        private const int WebSocketMaxFrameSize = 4096;
        private const string ProtocolHeaderErrorMessage = "Произошла ошибка десериализации заголовка от удалённой стороны.";
        /// <summary>
        /// Содержит имена методов прокси интерфейса без постфикса Async.
        /// </summary>
        private protected abstract IConcurrentDictionary<MethodInfo, string> _proxyMethodName { get; }
        /// <summary>
        /// Содержит все доступные для вызова экшены контроллеров.
        /// </summary>
        private readonly ControllerActionsDictionary _controllers;
        /// <summary>
        /// Для <see cref="Task"/> <see cref="Completion"/>.
        /// </summary>
        private readonly TaskCompletionSource<Exception> _completionTcs = new TaskCompletionSource<Exception>();
        private readonly bool _isServer;
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
        private readonly Channel<SerializedMessageToSend> _sendChannel;
        private int _disposed;
        private bool IsDisposed => Volatile.Read(ref _disposed) == 1;
        /// <summary>
        /// <see langword="true"/> если происходит остановка сервиса.
        /// </summary>
        private volatile bool _stopRequired;
        /// <summary>
        /// Возвращает <see cref="Task"/> который завершается когда 
        /// соединение переходит в закрытое состояние.
        /// Не бросает исключения.
        /// Причину разъединения можно узнать у свойства <see cref="DisconnectReason"/>.
        /// </summary>
        public Task<Exception> Completion => _completionTcs.Task;
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
                Exception disconnectReason = null;
                lock (_disconnectEventObj)
                {
                    if (_disconnectReason == null)
                    {
                        _Disconnected += value;
                    }
                    else
                    // Подписка к уже отключенному сокету.
                    {
                        disconnectReason = _disconnectReason;
                    }
                }

                if(disconnectReason != null)
                {
                    value(this, new SocketDisconnectedEventArgs(disconnectReason));
                }
            }
            remove
            {
                // Отписываться можно без блокировки — делегаты потокобезопасны.
                _Disconnected -= value;
            }
        }
        /// <summary>
        /// Истиная причина обрыва соединения. 
        /// <see langword="volatile"/> нужен для тандема с <see langword="volatile"/> <see cref="IsConnected"/>.
        /// </summary>
        private volatile Exception _disconnectReason;
        /// <summary>
        /// Причина обрыва соединения; <see langword="volatile"/>.
        /// </summary>
        public Exception DisconnectReason => _disconnectReason;
        //internal event EventHandler<Controller> BeforeInvokeController;
        private protected abstract void BeforeInvokeController(Controller controller);
        private volatile bool _isConnected = true;
        /// <summary>
        /// <see langword="volatile"/>; Если значение <see langword="false"/>, то можно узнать причину через свойство <see cref="DisconnectReason"/>.
        /// </summary>
        public bool IsConnected => _isConnected;

        // static ctor.
        static ManagedConnection()
        {
            //ManagedWebSocket.DefaultNoDelay = true;

            // Прогрев сериализатора.
            ProtoBuf.Serializer.PrepareSerializer<HeaderDto>();
            ExtensionMethods.WarmupRequestMessageSerialization();
        }

        // ctor.
        /// <summary>
        /// 
        /// </summary>
        internal ManagedConnection(MyWebSocket clientConnection, bool isServer, ServiceProvider serviceProvider, ControllerActionsDictionary controllers)
        {
            _isServer = isServer;

            Socket = new SocketWrapper(clientConnection);

            // IoC готов к работе.
            ServiceProvider = serviceProvider;

            // Копируем список контроллеров сервера.
            _controllers = controllers;

            // Запустить диспетчер отправки сообщений.
            _sendChannel = Channel.CreateUnbounded<SerializedMessageToSend>(new UnboundedChannelOptions
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
                ((ManagedConnection)state).SenderLoop();
            }, this); // Без замыкания.
        }

        /// <summary>
        /// Запрещает отправку новых запросов и приводит к остановке когда обработаются ожидающие запросы.
        /// Взводит <see cref="Completion"/>.
        /// Не бросает исключения.
        /// </summary>
        internal void RequireStop()
        {
            // volatile.
            _stopRequired = true;
            
            if(Interlocked.Decrement(ref _reqAndRespCount) == -1)
            // Нет ни одного ожадающего запроса.
            {
                // Можно безопасно остановить сокет.
                Dispose(new StopRequiredException());
            }
            // Иначе другие потоки уменьшив переменную увидят что флаг стал -1
            // Это будет соглашением о необходимости остановки.
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        internal object OnServerProxyCall(MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Тип результата инкапсулированный в Task<T>.
            Type resultType = targetMethod.GetMethodReturnType();

            // Имя метода без постфикса Async.
            string remoteMethodName = GetProxyMethodName(targetMethod);

            // Создаём запрос для отправки.
            var requestToSend = new RequestMessage(resultType, $"{controllerName}/{remoteMethodName}", Message.PrepareArgs(args));

            // Сериализуем запрос в память.
            SerializedMessageToSend serMsg = SerializeRequest(requestToSend);

            // Отправляем запрос.
            Task<object> taskObject = OnProxyCall(serMsg, requestToSend);

            return OnProxyCallConvert(targetMethod, resultType, taskObject);
        }

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу.
        /// </summary>
        internal static object OnClientProxyCallStatic(ValueTask<ManagedConnection> contextTask, MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Тип результата инкапсулированный в Task<T>.
            Type resultType = targetMethod.GetMethodReturnType();

            // Имя метода без постфикса Async.
            string remoteMethodName = ClientSideConnection.ProxyMethodName.GetOrAdd(targetMethod, m => m.GetNameTrimAsync());

            // Создаём запрос для отправки.
            var requestToSend = new RequestMessage(resultType, $"{controllerName}/{remoteMethodName}", Message.PrepareArgs(args));

            // Сериализуем запрос в память. Лучше выполнить до подключения.
            SerializedMessageToSend serMsg = SerializeRequest(requestToSend);

            // Отправляем запрос.
            Task<object> taskObject = OnProxyCallAsync(contextTask, serMsg, requestToSend);

            return OnProxyCallConvert(targetMethod, resultType, taskObject);
        }

        private static async Task<object> OnProxyCallAsync(ValueTask<ManagedConnection> contextTask, SerializedMessageToSend serializedMessage, RequestMessage requestMessage)
        {
            ManagedConnection context;
            if (contextTask.IsCompletedSuccessfully)
                context = contextTask.Result;
            else
                context = await contextTask.ConfigureAwait(false);

            // Отправляет запрос и получает результат от удалённой стороны.
            return await context.OnProxyCall(serializedMessage, requestMessage).ConfigureAwait(false);
        }

        private static object OnProxyCallConvert(MethodInfo targetMethod, Type resultType, Task<object> taskObject)
        {
            // Если возвращаемый тип функции интерфейса — Task.
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

        /// <summary>
        /// Происходит при обращении к проксирующему интерфейсу. Отправляет запрос и ожидает его ответ.
        /// </summary>
        /// /// <param name="resultType">Тип в который будет десериализован результат запроса.</param>
        internal async Task<object> OnProxyCall(SerializedMessageToSend serializedMessage, RequestMessage requestMessage)
        {
            ThrowIfDisposed();
            ThrowIfStopRequired();

            // Добавить запрос в словарь для дальнейшей связки с ответом.
            RequestAwaiter tcs = Socket.PendingRequests.AddRequest(requestMessage, out ushort uid);

            // Назначить запросу уникальный идентификатор.
            serializedMessage.Uid = uid;

            // Планируем отправку запроса.
            QueueSendMessage(serializedMessage);

            // Ожидаем результат от потока поторый читает из сокета.
            object rawResult = await tcs;

            // Успешно получили результат без исключений.
            return rawResult;
        }

        /// <summary>
        /// Возвращает имя метода без постфикса Async.
        /// </summary>
        /// <param name="method"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetProxyMethodName(MethodInfo method)
        {
            return _proxyMethodName.GetOrAdd(method, valueFactory: m => m.GetNameTrimAsync());
        }

        /// <summary>
        /// Запускает бесконечный цикл, в фоновом потоке, считывающий из сокета запросы и ответы.
        /// </summary>
        internal void StartReceivingLoop()
        {
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                // Не бросает исключения.
                ((ManagedConnection)state).ReceiveLoop();
                
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
                if (webSocketMessage.ReceiveResult.Success)
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
                    AtomicDisconnect(webSocketMessage.ReceiveResult.ToException());

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

                        if (webSocketMessage.ReceiveResult.Success)
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
                            AtomicDisconnect(webSocketMessage.ReceiveResult.ToException());

                            // Завершить поток.
                            return;
                        }

                        #endregion

                    } while (!webSocketMessage.EndOfMessage);

                    if (header != null)
                    {
                        #region Обработка Payload

                        if (header.StatusCode == StatusCode.Request)
                        // Получен запрос.
                        {
                            #region Выполнение запроса

                            #region Десериализация запроса

                            RequestMessageDto receivedRequest;
                            try
                            {
                                // Десериализуем запрос.
                                //receivedRequest = ExtensionMethods.DeserializeRequestJson(messageStream);
                                receivedRequest = ExtensionMethods.DeserializeRequestBson(messageStream);
                            }
                            catch (Exception ex)
                            // Ошибка десериализации запроса.
                            {
                                #region Игнорируем запрос

                                // Подготовить ответ с ошибкой.
                                var errorResponse = new ResponseMessage(header.Uid, new InvalidRequestResult($"Не удалось десериализовать запрос. Ошибка: \"{ex.Message}\"."));

                                // Передать на отправку результат с ошибкой, в другом потоке.
                                QueueSendResponse(errorResponse);

                                // Вернуться к получению заголовка.
                                continue;
                                #endregion
                            }
                            #endregion

                            #region Выполнение запроса

                            // Установить контекст запроса.
                            var request = new RequestContext(header, receivedRequest);

                            // Начать выполнение запроса в отдельном потоке.
                            StartProcessRequest(request);
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

                                    if (reqAwaiter.Request.ReturnType != typeof(void))
                                    {
                                        // Десериализатор в соответствии с ContentEncoding.
                                        var deserializer = header.GetDeserializer();

                                        bool deserialized;
                                        object rawResult = null;
                                        try
                                        {
                                            rawResult = deserializer(messageStream, reqAwaiter.Request.ReturnType);
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
                                if (Interlocked.Decrement(ref _reqAndRespCount) != -1)
                                // Был запрос на остановку.
                                {
                                    continue;
                                }
                                else
                                {
                                    AtomicSetCompletion(new StopRequiredException());
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
        /// Сериализует сообщение в новом потоке и добавляет в очередь на отправку.
        /// Не должно бросать исключения(!).
        /// </summary>
        /// <param name="responseToSend"></param>
        private void QueueSendResponse(ResponseMessage responseToSend)
        {
            ThreadPool.UnsafeQueueUserWorkItem(state => 
            {
                var tuple = ((ManagedConnection thisRef, ResponseMessage responseToSend))state;

                // Сериализуем.
                SerializedMessageToSend serializedMessage = SerializeResponse(tuple.responseToSend);

                // Ставим в очередь.
                tuple.thisRef.QueueSendMessage(serializedMessage);

            }, (this, responseToSend));
        }

        /// <summary>
        /// Добавляет хэдер и передает на отправку другому потоку.
        /// Не бросает исключения.
        /// </summary>
        private void QueueSendMessage(SerializedMessageToSend messageToSend)
        {
            // На текущем этапе сокет может быть уже уничтожен другим потоком.
            // В этом случае можем беспоследственно проигнорировать отправку; вызывающий получит исключение через RequestAwaiter.
            if (!Socket.IsDisposed)
            {
                // Сериализуем хедер. Не бросает исключения.
                AppendHeader(messageToSend);
                
                // Из-за AllowSynchronousContinuations частично начнёт отправку текущим потоком(!).
                if (_sendChannel.Writer.TryWrite(messageToSend))
                    return;
                else
                    messageToSend.Dispose(); // Канал уже закрыт (был вызван Dispose).
            }
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        private static SerializedMessageToSend SerializeRequest(RequestMessage requestToSend)
        {
            var serMsg = new SerializedMessageToSend(requestToSend);
            try
            {
                var request = new RequestMessageDto
                {
                    ActionName = requestToSend.ActionName,
                    Args = requestToSend.Args,
                };
                //ExtensionMethods.SerializeObjectJson(serMsg.MemoryStream, request);
                ExtensionMethods.SerializeObjectBson(serMsg.MemoryStream, request);
            }
            catch
            {
                serMsg.Dispose();
                throw;
            }
            return serMsg;
        }

        /// <summary>
        /// Сериализует сообщение в память. Может бросить исключение сериализации.
        /// </summary>
        private static SerializedMessageToSend SerializeResponse(ResponseMessage responseToSend)
        {
            var serMsg = new SerializedMessageToSend(responseToSend);
            try
            {
                if (responseToSend.Result is IActionResult actionResult)
                {
                    // Сериализуем ответ.
                    actionResult.ExecuteResult(new ActionContext(responseToSend.ReceivedRequest, serMsg.MemoryStream));
                }
                else
                {
                    // Сериализуем ответ.
                    responseToSend.ReceivedRequest.ActionToInvoke.Serializer(serMsg.MemoryStream, responseToSend.Result);
                    serMsg.ContentEncoding = responseToSend.ReceivedRequest.ActionToInvoke.ProducesEncoding;
                    serMsg.StatusCode = StatusCode.Ok;
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
            header.SerializeProtoBuf(messageToSend.MemoryStream, out int headerSize);

            // Запомним размер хэдера.
            messageToSend.HeaderSize = headerSize;
        }

        private static HeaderDto CreateHeader(SerializedMessageToSend messageToSend)
        {
            if (messageToSend.MessageToSend is ResponseMessage responseToSend)
            {
                return new HeaderDto(responseToSend.Uid, messageToSend.StatusCode.Value, (int)messageToSend.MemoryStream.Length)
                {
                    ContentEncoding = messageToSend.ContentEncoding
                };
            }
            else
            // Ответ на запрос.
            {
                return new HeaderDto(messageToSend.Uid, StatusCode.Request, (int)messageToSend.MemoryStream.Length);
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
                if (await _sendChannel.Reader.WaitToReadAsync())
                {
                    // Всегда true — у нас только один читатель.
                    _sendChannel.Reader.TryRead(out SerializedMessageToSend serializedMessage);

                    // Теперь мы владеем этим объектом.
                    using (serializedMessage)
                    {
                        if (serializedMessage.MessageToSend is RequestMessage)
                        {
                            // Должны получить ответ на этот запрос.
                            if (Interlocked.Increment(ref _reqAndRespCount) == 0)
                            // Значение было -1, значит происходит остановка и сокет уже уничтожен.
                            {
                                return;
                            }
                        }

                        byte[] streamBuffer = serializedMessage.MemoryStream.DangerousGetBuffer();

                        // Размер сообщения без заголовка.
                        int messageSize = (int)serializedMessage.MemoryStream.Length - serializedMessage.HeaderSize;

                        #region Отправка заголовка

                        SocketError socketError;
                        try
                        {
                            // Заголовок лежит в конце стрима.
                            socketError = await Socket.WebSocket.SendExAsync(streamBuffer.AsMemory(messageSize, serializedMessage.HeaderSize),
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
                        #endregion

                        if (socketError == SocketError.Success)
                        {
                            #region Отправка тела сообщения (запрос или ответ на запрос)

                            // Отправляем сообщение по частям.
                            int offset = 0;
                            int bytesLeft = messageSize;
                            do
                            {
                                // TODO возможно нет смысла.
                                #region Фрагментируем отправку

                                bool endOfMessage;
                                int countToSend = WebSocketMaxFrameSize;
                                if (countToSend >= bytesLeft)
                                {
                                    countToSend = bytesLeft;
                                    endOfMessage = true;
                                }
                                else
                                    endOfMessage = false;

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
                                    if (endOfMessage)
                                        break;

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
                            } while (bytesLeft > 0);
                            #endregion
                        }
                        else
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(socketError.ToException());

                            // Завершить поток.
                            return;
                        }

                        if (serializedMessage.MessageToSend is ResponseMessage)
                        // Ответ успешно отправлен.
                        {
                            if (Interlocked.Decrement(ref _reqAndRespCount) > -1)
                            {
                                continue;
                            }
                            else
                            // Был заказ на остановку.
                            {
                                AtomicSetCompletion(new StopRequiredException());
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

        /// <summary>
        /// Потокобезопасно взводит <see cref="Task"/> <see cref="Completion"/>.
        /// </summary>
        private void AtomicSetCompletion(Exception disconnectReason)
        {
            _completionTcs.TrySetResult(disconnectReason);
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
                EventHandler<SocketDisconnectedEventArgs> disconnected;
                lock (_disconnectEventObj)
                {
                    // Запомнить истинную причину обрыва.
                    _disconnectReason = disconnectReason;

                    // Установить флаг после причины обрыва.
                    _isConnected = false;

                    // Скопируем делегат что-бы вызывать не в блокировке — на всякий случай.
                    disconnected = _Disconnected;

                    // Теперь можно безопасно убрать подписчиков.
                    _Disconnected = null;
                }

                // Установить Task Completed.
                AtomicSetCompletion(disconnectReason);

                // Сообщить об обрыве.
                disconnected?.Invoke(this, new SocketDisconnectedEventArgs(disconnectReason));
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
        /// Результатом может быть IActionResult или Raw объект или исключение.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        private async ValueTask<object> InvokeControllerAsync(RequestContext receivedRequest)
        {
            // Находим контроллер по словарю без блокировки.
            if (TryGetRequestedController(receivedRequest, out string controllerName, out string actionName, out Type controllerType))
            {
                // Ищем делегат запрашиваемой функции по словарю без блокировки.
                if (_controllers.TryGetValue(controllerType, actionName, out ControllerAction action))
                {
                    // Контекст запроса запоминает запрашиваемый метод.
                    receivedRequest.ActionToInvoke = action;

                    // Проверить доступ к функции.
                    if (InvokeMethodPermissionCheck(action.TargetMethod, controllerType, out IActionResult permissionError))
                    {
                        // Блок IoC выполнит Dispose всем созданным экземплярам.
                        using (IServiceScope scope = ServiceProvider.CreateScope())
                        {
                            // Активируем контроллер через IoC.
                            var controller = (Controller)scope.ServiceProvider.GetRequiredService(controllerType);
                            //{
                            // Подготавливаем контроллер.
                            BeforeInvokeController(controller);

                            // Мапим и десериализуем аргументы по их именам.
                            //object[] args = DeserializeParameters(action.TargetMethod.GetParameters(), receivedRequest);

                            // Мапим и десериализуем аргументы по их порядку.
                            object[] args = DeserializeArguments(action.TargetMethod.GetParameters(), receivedRequest.RequestDto);

                            // Вызов метода контроллера.
                            object controllerResult = action.TargetMethod.InvokeFast(controller, args);

                            if (controllerResult != null)
                            {
                                // Извлекает результат из Task'а.
                                ValueTask<object> controllerResultTask = DynamicAwaiter.WaitAsync(controllerResult);

                                if (controllerResultTask.IsCompletedSuccessfully)
                                    controllerResult = controllerResultTask.Result;
                                else
                                    controllerResult = await controllerResultTask;
                            }

                            // Результат успешно получен без исключения.
                            return controllerResult;
                            //}
                        }
                    }
                    else
                        return permissionError;
                }
                else
                    return new NotFoundResult($"Unable to find requested action \"{receivedRequest.RequestDto.ActionName}\".");
            }
            else
                return new NotFoundResult($"Unable to find requested controller \"{controllerName}\".");
        }

        /// <summary>
        /// Проверяет доступность запрашиваемого метода для удаленного пользователя.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        protected abstract bool InvokeMethodPermissionCheck(MethodInfo method, Type controllerType, out IActionResult permissionError);

        /// <summary>
        /// Пытается найти запрашиваемый пользователем контроллер.
        /// </summary>
        private bool TryGetRequestedController(RequestContext request, out string controllerName, out string actionName, out Type controllerType)
        {
            int index = request.RequestDto.ActionName.IndexOf('/');
            if (index == -1)
            {
                controllerName = "Home";
                actionName = request.RequestDto.ActionName;
            }
            else
            {
                controllerName = request.RequestDto.ActionName.Substring(0, index);
                actionName = request.RequestDto.ActionName.Substring(index + 1);
            }

            // Ищем контроллер в кэше.
            if (_controllers.Controllers.TryGetValue(controllerName, out controllerType))
                return true;

            return false;
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

        // TODO заменить исключение.
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
        private void StartProcessRequest(RequestContext request)
        {
            ThreadPool.UnsafeQueueUserWorkItem(state =>
            {
                var tuple = ((ManagedConnection context, RequestContext request))state;

                // Не бросает исключения.
                tuple.context.StartProcessRequestThread(tuple.request);

            }, state: (this, request)); // Без замыкания.
        }

        private async void StartProcessRequestThread(RequestContext receivedRequest)
        // Новый поток из пула потоков.
        {
            // Увеличить счетчик запросов.
            if (Interlocked.Increment(ref _reqAndRespCount) > 0)
            {
                // Не бросает исключения.
                // Выполняет запрос и возвращает ответ.
                ValueTask<SerializedMessageToSend> responseToSendTask = GetResponseAsync(receivedRequest);

                SerializedMessageToSend responseToSend;
                if (responseToSendTask.IsCompletedSuccessfully)
                    responseToSend = responseToSendTask.Result;
                else
                    responseToSend = await responseToSendTask;

                // Не бросает исключения.
                QueueSendMessage(responseToSend);
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
        private async ValueTask<SerializedMessageToSend> GetResponseAsync(RequestContext receivedRequest)
        {
            // Результат контроллера. Может быть Task.
            ResponseMessage response;
            try
            {
                // Находит и выполняет запрашиваемую функцию.
                ValueTask<object> rawResultTask = InvokeControllerAsync(receivedRequest);

                object rawResult;
                if (rawResultTask.IsCompletedSuccessfully)
                    rawResult = rawResultTask.Result;
                else
                    rawResult = await rawResultTask;

                response = new ResponseMessage(receivedRequest, rawResult);
            }
            catch (BadRequestException ex)
            {
                // Вернуть результат с ошибкой.
                response = new ResponseMessage(receivedRequest, new BadRequestResult(ex.Message));
            }
            catch (Exception ex)
            // Злая ошибка обработки запроса. Аналогично ошибке 500.
            {
                // Прервать отладку.
                DebugOnly.Break();

                Debug.WriteLine(ex);

                // Вернуть результат с ошибкой.
                response = new ResponseMessage(receivedRequest, new InternalErrorResult("Internal Server Error"));
            }

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

                    Debug.WriteLine(ex);

                    // Вернуть результат с ошибкой.
                    response = new ResponseMessage(receivedRequest, new InternalErrorResult("Internal Server Error"));
                }

                // Запрашиваемая функция выполнена успешно.
                return SerializeResponse(response);
            }
            else
            {
                // Содержит ошибку; Сериализуется без исключения.
                return SerializeResponse(response);
            }
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
            if (!_stopRequired)
                return;

            throw new StopRequiredException();
        }

        /// <summary>
        /// Вызывает Dispose распространяя исключение <see cref="StopRequiredException"/> другим потокам.
        /// Потокобезопасно.
        /// </summary>
        internal void CloseAndDispose()
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
