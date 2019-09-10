using DanilovSoft;
using DanilovSoft.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
//using MyClientWebSocket = DanilovSoft.WebSocket.ClientWebSocket;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Контекст клиентского соединения.
    /// </summary>
    [DebuggerDisplay(@"\{Connected = {IsConnected}\}")]
    public sealed class RpcClient : IDisposable
    {
        /// <summary>
        /// Используется для синхронизации установки соединения.
        /// </summary>
        private readonly ChannelLock _connectLock;
        /// <summary>
        /// Адрес для подключеия к серверу.
        /// </summary>
        private readonly Uri _uri;
        private readonly ControllerActionsDictionary _controllers;
        private readonly ProxyCache _proxyCache = new ProxyCache();
        /// <summary>
        /// <see langword="volatile"/>.
        /// </summary>
        private ApplicationBuilder _appBuilder;
        public ServiceProvider ServiceProvider { get; private set; }
        private Action<ApplicationBuilder> _configureApp;
        /// <summary>
        /// Устанавливается в блокировке <see cref="StateLock"/>.
        /// </summary>
        private volatile ClientSideConnection _connection;
        /// <summary>
        /// Завершается если подключение разорвано или не установлено.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion => _connection?.Completion ?? Task.FromResult(CloseReason.NoConnectionError);
        public bool IsConnected => _connection?.IsConnected ?? false;
        private StopRequired _stopRequired;
        //public StopRequired StopRequired => _stopRequired;
        private bool _disposed;
        /// <summary>
        /// Для доступа к <see cref="_disposed"/> и <see cref="_stopRequired"/>.
        /// </summary>
        private object StateLock => _proxyCache;
        //public RpcClientState State { get; private set; }
        /// <summary>
        /// Используется только что-бы аварийно прервать подключение через Dispose.
        /// </summary>
        private ClientWebSocket _connectingWs;

        static RpcClient()
        {
            Warmup.DoWarmup();
        }

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        public RpcClient(Uri uri) : this(Assembly.GetCallingAssembly(), uri)
        {

        }

        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        public RpcClient(string host, int port) : this(Assembly.GetCallingAssembly(), new Uri($"ws://{host}:{port}"))
        {
            
        }

        /// <summary>
        /// Конструктор клиента.
        /// </summary>
        /// <param name="controllersAssembly">Сборка в которой осуществляется поиск контроллеров.</param>
        /// <param name="uri">Адрес сервера.</param>
        private RpcClient(Assembly controllersAssembly, Uri uri)
        {
            Debug.Assert(controllersAssembly != Assembly.GetExecutingAssembly());

            // Словарь с найденными контроллерами в вызывающей сборке.
            _controllers = new ControllerActionsDictionary(GlobalVars.FindAllControllers(controllersAssembly));
            _uri = uri;
            _connectLock = new ChannelLock();
        }

        /// <summary>
        /// Позволяет настроить IoC контейнер.
        /// Выполняется единожды при инициализации подключения.
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            ThrowIfDisposed();

            if (ServiceProvider != null)
                throw new InvalidOperationException("Service already configured.");

            var serviceCollection = new ServiceCollection();
            configure(serviceCollection);
            ServiceProvider = ConfigureIoC(serviceCollection);
        }

        /// <exception cref="ObjectDisposedException"/>
        public void Configure(Action<ApplicationBuilder> configureApp)
        {
            ThrowIfDisposed();

            _configureApp = configureApp;
        }

        /// <summary>
        /// Блокирует поток до завершения <see cref="Completion"/>.
        /// </summary>
        public CloseReason WaitCompletion()
        {
            return Completion.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public ConnectResult Connect()
        {
            return ConnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public Task<ConnectResult> ConnectAsync()
        {
            ThrowIfDisposed();

            var t = ConnectIfNeededAsync();
            if(t.IsCompleted)
            {
                ConnectionResult conRes = t.Result;
                return Task.FromResult(Result(conRes));
            }
            else
            {
                return WaitForConnectAsync(t);
            }

            static async Task<ConnectResult> WaitForConnectAsync(ValueTask<ConnectionResult> t)
            {
                ConnectionResult conRes = await t.ConfigureAwait(false);
                return Result(conRes);
            }

            static ConnectResult Result(in ConnectionResult conRes)
            {
                if (conRes.Connection != null)
                    return new ConnectResult(ConnectState.Connected, conRes.SocketError);
                else if (conRes.SocketError != null)
                    return new ConnectResult(ConnectState.RetryLater, conRes.SocketError);
                else
                    return new ConnectResult(ConnectState.Stopped, conRes.SocketError);
            }
        }

        /// <summary>
        /// Создаёт прокси из интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public T GetProxy<T>()
        {
            return _proxyCache.GetProxy<T>(ContextCallback);
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="timeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public CloseReason Stop(TimeSpan timeout, string closeDescription = null)
        {
            return StopAsync(timeout, closeDescription).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Начинает грациозную остановку. Не блокирует поток.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public void BeginStop(TimeSpan timeout, string closeDescription = null)
        {
            PrivateStopAsync(timeout, closeDescription).GetAwaiter();
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="timeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public Task<CloseReason> StopAsync(TimeSpan timeout, string closeDescription = null)
        {
            return PrivateStopAsync(timeout, closeDescription);
        }

        private async Task<CloseReason> PrivateStopAsync(TimeSpan timeout, string closeDescription)
        {
            bool created;
            StopRequired stopRequired;
            ClientSideConnection connection;
            lock (StateLock)
            {
                stopRequired = _stopRequired;
                if (stopRequired == null)
                {
                    stopRequired = new StopRequired(timeout, closeDescription);
                    _stopRequired = stopRequired;
                    created = true;

                    // Скопировать пока мы в блокировке.
                    connection = _connection;
                }
                else
                {
                    created = false;
                    connection = null;
                }
            }

            CloseReason closeReason;

            if (created)
            // Только один поток зайдёт сюда.
            {
                if (connection != null)
                // Существует живое соединение.
                {
                    closeReason = await connection.StopAsync(stopRequired).ConfigureAwait(false);
                }
                else
                // Соединения не существует и новые создаваться не смогут.
                {
                    // Передать результат другим потокам которые повторно вызовут Stop.
                    closeReason = stopRequired.SetTaskAndReturn(CloseReason.NoConnectionGracifully);
                }
            }
            else
            // Другой поток уже начал остановку.
            {
                closeReason = await stopRequired.Task.ConfigureAwait(false);
            }

            return closeReason;
        }

        /// <summary>
        /// Возвращает существующее подключение или создаёт новое, когда 
        /// происходит вызов метода интерфеса.
        /// </summary>
        private ValueTask<ManagedConnection> ContextCallback()
        {
            // volatile копия.
            var stopRequired = _stopRequired;

            if (stopRequired == null)
            {
                // Копия volatile.
                var context = _connection;

                if (context != null)
                // Есть живое соединение.
                {
                    return new ValueTask<ManagedConnection>(context);
                }
                else
                // Нужно установить подключение.
                {
                    var t = ConnectIfNeededAsync();
                    if (t.IsCompleted)
                    {
                        ConnectionResult connectionResult = t.Result;

                        if (connectionResult.Connection != null)
                            return new ValueTask<ManagedConnection>(connectionResult.Connection);
                        else if (connectionResult.SocketError != null)
                            return new ValueTask<ManagedConnection>(Task.FromException<ManagedConnection>(connectionResult.SocketError.Value.ToException()));
                        else
                            return new ValueTask<ManagedConnection>(Task.FromException<ManagedConnection>(new StopRequiredException(connectionResult.StopState)));
                    }
                    else
                    {
                        return WaitForConnectIfNeededAsync(t);
                    }
                }
            }
            else
            // Был запрос на остановку.
            {
                return new ValueTask<ManagedConnection>(
                   Task.FromException<ManagedConnection>(new StopRequiredException(stopRequired)));
            }
        }

        private static async ValueTask<ManagedConnection> WaitForConnectIfNeededAsync(ValueTask<ConnectionResult> t)
        {
            ConnectionResult connectionResult = await t.ConfigureAwait(false);

            if (connectionResult.Connection != null)
                return connectionResult.Connection;
            else if (connectionResult.SocketError != null)
                throw connectionResult.SocketError.Value.ToException();
            else
                throw new StopRequiredException(connectionResult.StopState);
        }

        /// <summary>
        /// Событие — обрыв сокета. Потокобезопасно. Срабатывает только один раз.
        /// </summary>
        private void Disconnected(object sender, SocketDisconnectedEventArgs e)
        {
            // volatile.
            _connection = null;
        }

        /// <summary>
        /// Выполнить подключение сокета если еще не подключен.
        /// </summary>
        private ValueTask<ConnectionResult> ConnectIfNeededAsync()
        {
            // Копия volatile.
            var context = _connection;

            if (context != null)
            // Есть живое соединение.
            {
                return new ValueTask<ConnectionResult>(new ConnectionResult(null, null, context));
            }
            else
            // Подключение отсутствует.
            {
                // Захватить блокировку.
                var t = _connectLock.LockAsync();
                if (t.IsCompleted)
                {
                    ChannelLock.Releaser releaser = t.Result;
                    return LockAquiredConnectAsync(releaser);
                }
                else
                {
                    return WaitForLockAndConnectAsync(t);
                }
            }
        }

        private async ValueTask<ConnectionResult> WaitForLockAndConnectAsync(ValueTask<ChannelLock.Releaser> t)
        {
            ChannelLock.Releaser releaser = await t.ConfigureAwait(false);
            return await LockAquiredConnectAsync(releaser).ConfigureAwait(false);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="StopRequiredException"/>
        /// <exception cref="ObjectDisposedException"/>
        private async ValueTask<ConnectionResult> LockAquiredConnectAsync(ChannelLock.Releaser conLock)
        {
            using (conLock)
            {
                // Копия volatile.
                var connection = _connection;

                if (connection == null)
                {
                    ServiceProvider serviceProvider = ServiceProvider;
                    lock (StateLock)
                    {
                        if (!_disposed)
                        {
                            if (_stopRequired != null)
                            {
                                // Нельзя создавать новое подключение если был вызван Stop.
                                return new ConnectionResult(null, _stopRequired, null);
                            }
                            else
                            {
                                if (serviceProvider == null)
                                {
                                    var serviceCollection = new ServiceCollection();
                                    serviceProvider = ConfigureIoC(serviceCollection);
                                    ServiceProvider = serviceProvider;
                                }
                            }
                        }
                        else
                        {
                            throw new ObjectDisposedException(GetType().FullName);
                        }
                    }

                    _appBuilder = new ApplicationBuilder(serviceProvider);
                    _configureApp?.Invoke(_appBuilder);

                    // Новый сокет.
                    var ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = _appBuilder.KeepAliveInterval;
                    ws.Options.ReceiveTimeout = _appBuilder.ReceiveTimeout;

                    ReceiveResult receiveResult;

                    // Позволить Dispose прервать подключение.
                    Interlocked.Exchange(ref _connectingWs, ws);

                    try
                    {
                        // Обычное подключение Tcp.
                        receiveResult = await ws.ConnectExAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    // Не удалось подключиться (сервер не запущен или вызван Dispose).
                    {
                        ws.Dispose();
                        Debug.WriteLine(ex);
                        throw;
                    }

                    if(Interlocked.Exchange(ref _connectingWs, null) == null)
                    // Dispose убил наш экземпляр.
                    {
                        // Больше ничего делать не нужно.
                        throw new ObjectDisposedException(GetType().FullName);
                    }

                    if (receiveResult.IsReceivedSuccessfully)
                    // Соединение успешно установлено.
                    {
                        StopRequired stopRequired = null;
                        lock (StateLock)
                        {
                            if (!_disposed)
                            {
                                connection = new ClientSideConnection(this, ws, serviceProvider, _controllers);

                                // Скопировать пока мы в блокировке.
                                stopRequired = _stopRequired;

                                if (stopRequired == null)
                                {
                                    // Косвенно устанавливает флаг IsConnected.
                                    _connection = connection;
                                }
                            }
                            else
                            // Был выполнен Dispose в тот момент когда велась попытка установить соединение.
                            {
                                // В этом случае лучше просто закрыть соединение.
                                ws.Dispose();
                                throw new ObjectDisposedException(GetType().FullName);
                            }
                        }

                        if (stopRequired == null)
                        // Запроса на остановку сервиса ещё не было.
                        {
                            connection.InitStartThreads();
                            connection.Disconnected += Disconnected;
                            return new ConnectionResult(receiveResult.SocketError, null, connection);
                        }
                        else
                        // Был запрос на остановку сервиса. 
                        // Он произошел в тот момент когда велась попытка установить соединение.
                        // Это очень редкий случай но мы должны его предусмотреть.
                        {
                            using (connection)
                            {
                                // Мы обязаны закрыть это соединение.
                                await connection.StopAsync(stopRequired).ConfigureAwait(false);
                            }

                            return new ConnectionResult(receiveResult.SocketError, stopRequired, null);
                            //throw new StopRequiredException(stopRequired);
                        }
                    }
                    else
                    // Подключение не удалось.
                    {
                        ws.Dispose();
                        return new ConnectionResult(receiveResult.SocketError, null, null);
                    }
                }
                else
                    return new ConnectionResult(SocketError.Success, null, connection);
            }
        }

        /// <summary>
        /// Добавляет в IoC контейнер контроллеры из сборки и компилирует контейнер.
        /// </summary>
        private ServiceProvider ConfigureIoC(ServiceCollection serviceCollection)
        {
            // Добавим в IoC все контроллеры сборки.
            foreach (Type controllerType in _controllers.Controllers.Values)
                serviceCollection.AddScoped(controllerType);

            ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            return serviceProvider;
        }

        ///// <summary>
        ///// Отправляет специфический запрос содержащий токен авторизации. Ожидает ответ.
        ///// </summary>
        //private async Task<bool> AuthorizeAsync(SocketWrapper socketQueue, byte[] bearerToken)
        //{
        //    // Запрос на авторизацию по токену.
        //    var requestToSend = Message.CreateRequest("Auth/AuthorizeToken", new Arg[] { new Arg("token", bearerToken) });

        //    // Отправить запрос и получить ответ.
        //    object result = await ExecuteRequestAsync(requestToSend, returnType: typeof(bool), socketQueue).ConfigureAwait(false);

        //    return (bool)result;
        //}

        private void ThrowIfDisposed()
        {
            if (!_disposed)
                return;

            throw new ObjectDisposedException(GetType().FullName);
        }

        public void Dispose()
        {
            lock (StateLock)
            {
                if (!_disposed)
                {
                    _disposed = true;

                    // Прервать установку подключения если она выполняется.
                    Interlocked.Exchange(ref _connectingWs, null)?.Dispose();

                    _connection?.Dispose();
                    ServiceProvider?.Dispose();
                }
            }
        }
    }
}
