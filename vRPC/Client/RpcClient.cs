﻿using DanilovSoft.vRPC.Decorator;
using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{

    /// <summary>
    /// Контекст клиентского соединения.
    /// </summary>
    [DebuggerDisplay(@"\{{DebugDisplay,nq}\}")]
    public sealed class RpcClient : IDisposable, IGetProxy
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay
        {
            get
            {
                var state = State;
                if (state == RpcState.Open && IsAuthenticated)
                {
                    return $"{state}, Authenticated";
                }
                return state.ToString();
            }
        }
        /// <summary>
        /// Используется для синхронизации установки соединения.
        /// </summary>
        private readonly ChannelLock _connectLock;
        /// <summary>
        /// Адрес для подключения к серверу.
        /// </summary>
        private readonly InvokeActionsDictionary _invokeActions;
        private readonly ProxyCache _proxyCache;
        private readonly ServiceCollection _serviceCollection = new ServiceCollection();
        private bool IsAutoConnectAllowed { get; }
        public Uri ServerAddress { get; private set; }
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
        /// Завершается если подключение разорвано.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion { get; private set; }
        public RpcState State
        {
            get
            {
                if (_shutdownRequest != null)
                    return RpcState.ShutdownRequest;

                return _connection != null ? RpcState.Open : RpcState.Closed;
            }
        }
        /// <summary>
        /// volatile требуется лишь для публичного доступа.
        /// </summary>
        private volatile ShutdownRequest _shutdownRequest;
        /// <summary>
        /// Если был начат запрос на остновку, то это свойство будет содержать переданную причину остановки.
        /// Является <see langword="volatile"/>.
        /// </summary>
        public ShutdownRequest StopRequiredState => _shutdownRequest;
        private bool _disposed;
        /// <summary>
        /// Для доступа к <see cref="_disposed"/> и <see cref="_shutdownRequest"/>.
        /// </summary>
        private object StateLock => _proxyCache;
        /// <summary>
        /// Используется только что-бы аварийно прервать подключение через Dispose.
        /// </summary>
        private ClientWebSocket _connectingWs;
        /// <summary>
        /// True если соединение прошло аутентификацию на сервере.
        /// </summary>
        public bool IsAuthenticated => _connection?.IsAuthenticated ?? false;

        // ctor.
        static RpcClient()
        {
            Warmup.DoWarmup();
        }

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        /// <param name="allowAutoConnect">Разрешено ли интерфейсам самостоятельно устанавливать и повторно переподключаться к серверу.</param>
        public RpcClient(Uri serverAddress, bool allowAutoConnect = true) 
            : this(Assembly.GetCallingAssembly(), serverAddress, allowAutoConnect)
        {

        }

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        /// <param name="allowReconnect">Разрешено ли интерфейсам самостоятельно устанавливать и повторно переподключаться к серверу.</param>
        public RpcClient(string host, int port, bool ssl = false, bool allowReconnect = true) 
            : this(Assembly.GetCallingAssembly(), new Uri($"{(ssl ? "wss" : "ws")}://{host}:{port}"), allowReconnect)
        {
            
        }

        // ctor.
        /// <summary>
        /// Конструктор клиента.
        /// </summary>
        /// <param name="controllersAssembly">Сборка в которой осуществляется поиск контроллеров.</param>
        /// <param name="serverAddress">Адрес сервера.</param>
        private RpcClient(Assembly controllersAssembly, Uri serverAddress, bool allowAutoConnect)
        {
            Debug.Assert(controllersAssembly != Assembly.GetExecutingAssembly());

            // Найти все контроллеры в вызывающей сборке.
            Dictionary<string, Type> controllerTypes = GlobalVars.FindAllControllers(controllersAssembly);

            // Словарь с методами контроллеров.
            _invokeActions = new InvokeActionsDictionary(controllerTypes);
            ServerAddress = serverAddress;
            _connectLock = new ChannelLock();
            IsAutoConnectAllowed = allowAutoConnect;
            _proxyCache = new ProxyCache();

            InnerConfigureIoC(controllerTypes.Values);
        }

        /// <summary>
        /// Позволяет настроить IoC контейнер.
        /// Выполняется единожды при инициализации подключения.
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            ThrowIfDisposed();

            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            if (ServiceProvider != null)
                throw new InvalidOperationException("Service already configured.");

            configure(_serviceCollection);
            ServiceProvider = _serviceCollection.BuildServiceProvider();
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
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="HttpHandshakeException"/>
        /// <exception cref="ObjectDisposedException"/>
        public ConnectResult Connect()
        {
            return ConnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="HttpHandshakeException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Task<ConnectResult> ConnectAsync()
        {
            ThrowIfDisposed();
            ThrowIfWasShutdown();

            ValueTask<InnerConnectionResult> t = ConnectOrGetExistedConnectionAsync();
            if(t.IsCompletedSuccessfully)
            {
                InnerConnectionResult conRes = t.Result;
                return Task.FromResult(conRes.ToConnectResult());
            }
            else
            {
                return WaitForConnectAsync(t);
            }

            // Локальная.
            static async Task<ConnectResult> WaitForConnectAsync(ValueTask<InnerConnectionResult> t)
            {
                InnerConnectionResult conRes = await t.ConfigureAwait(false);
                return conRes.ToConnectResult();
            }
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="HttpHandshakeException"/>
        /// <exception cref="ObjectDisposedException"/>
        public ConnectResult Connect(AccessToken accessToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="HttpHandshakeException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Task<ConnectResult> ConnectAsync(AccessToken accessToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Выполняет аутентификацию текущего соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        /// <exception cref="ConnectionNotOpenException"/>
        public void SignIn(AccessToken accessToken)
        {
            SignInAsync(accessToken).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Выполняет аутентификацию текущего соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        /// <exception cref="ConnectionNotOpenException"/>
        public Task SignInAsync(AccessToken accessToken)
        {
            accessToken.ValidateAccessToken(nameof(accessToken));
            ThrowIfDisposed();
            ThrowIfWasShutdown();

            // Начать соединение или взять существующее.
            ValueTask<ClientSideConnection> connectionTask = GetOrOpenConnection();

            if (connectionTask.IsCompleted)
            {
                // Может бросить исключение.
                ClientSideConnection connection = connectionTask.Result;

                return connection.SignInAsync(accessToken);
            }
            else
            {
                return WaitConnection(connectionTask, accessToken);
            }

            static async Task WaitConnection(ValueTask<ClientSideConnection> t, AccessToken accessToken)
            {
                ClientSideConnection connection = await t.ConfigureAwait(false);
                await connection.SignInAsync(accessToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <exception cref="ConnectionNotOpenException"/>
        public void SignOut()
        {
            SignOutAsync().GetAwaiter().GetResult();
        }

        public Task SignOutAsync()
        {
            ThrowIfDisposed();
            ThrowIfWasShutdown();

            // Копия volatile.
            ClientSideConnection connection = _connection;

            if (connection != null)
            {
                return connection.SignOutAsync();  
            }
            else
            // Соединение закрыто и технически можно считать что операция выполнена успешно.
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Выполняет аутентификацию соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        public Task AuthenticateAsync(AccessToken accessToken)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// Полученный экземпляр можно привести к типу <see cref="ClientInterfaceProxy"/>.
        /// Метод является шорткатом для <see cref="GetProxyDecorator"/>
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public T GetProxy<T>() where T : class
        {
            return GetProxyDecorator<T>().Proxy;
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public ClientInterfaceProxy<T> GetProxyDecorator<T>() where T : class
        {
            var decorator = _proxyCache.GetProxyDecorator<T>(this);
            return decorator;
        }

        internal object OnInterfaceMethodCall(MethodInfo targetMethod, object[] args, string controllerName)
        {
            // Начать соединение или взять существующее.
            ValueTask<ClientSideConnection> connectionTask = GetOrOpenConnection();

            return ManagedConnection.OnClientProxyCallStatic(connectionTask, targetMethod, args, controllerName);
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует поток не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public CloseReason Shutdown(TimeSpan disconnectTimeout, string closeDescription = null)
        {
            return ShutdownAsync(disconnectTimeout, closeDescription).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Начинает грациозную остановку. Не блокирует поток.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public void BeginShutdown(TimeSpan disconnectTimeout, string closeDescription = null)
        {
            _ = PrivateShutdownAsync(disconnectTimeout, closeDescription);
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public Task<CloseReason> ShutdownAsync(TimeSpan disconnectTimeout, string closeDescription = null)
        {
            return PrivateShutdownAsync(disconnectTimeout, closeDescription);
        }

        private async Task<CloseReason> PrivateShutdownAsync(TimeSpan disconnectTimeout, string closeDescription)
        {
            bool created;
            ShutdownRequest stopRequired;
            ClientSideConnection connection;
            lock (StateLock)
            {
                stopRequired = _shutdownRequest;
                if (stopRequired == null)
                {
                    stopRequired = new ShutdownRequest(disconnectTimeout, closeDescription);

                    // Волатильно взводим флаг о необходимости остановки.
                    _shutdownRequest = stopRequired;
                    created = true;

                    // Прервать установку подключения если она выполняется.
                    Interlocked.Exchange(ref _connectingWs, null)?.Dispose();

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
                    closeReason = await connection.InnerShutdownAsync(stopRequired).ConfigureAwait(false);
                }
                else
                // Соединения не существует и новые создаваться не смогут.
                {
                    closeReason = CloseReason.NoConnectionGracifully;

                    // Передать результат другим потокам которые повторно вызовут Shutdown.
                    stopRequired.SetTaskResult(closeReason);
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
        /// Возвращает существующее подключение или создаёт новое если это разрешает свойство <see cref="IsAutoConnectAllowed"/>.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ConnectionNotOpenException"/>
        internal ValueTask<ClientSideConnection> GetOrOpenConnection()
        {
            // Копия volatile.
            ClientSideConnection connection = _connection;

            if (connection != null)
            // Есть живое соединение.
            {
                return new ValueTask<ClientSideConnection>(connection);
            }
            else
            // Нужно установить подключение.
            {
                if (IsAutoConnectAllowed)
                {
                    if (!TryGetShutdownException(out ValueTask<ClientSideConnection> shutdownException))
                    {
                        ValueTask<InnerConnectionResult> t = ConnectOrGetExistedConnectionAsync();
                        if (t.IsCompletedSuccessfully)
                        {
                            InnerConnectionResult connectionResult = t.Result; // Взять успешный результат.
                            return connectionResult.ToManagedConnectionTask();
                        }
                        else
                        {
                            return WaitForConnectionAsync(t);
                        }
                    }
                    else
                    // Уже был вызван Shutdown.
                    {
                        return shutdownException;
                    }
                }
                else
                {
                    return new ValueTask<ClientSideConnection>(Task.FromException<ClientSideConnection>(new ConnectionNotOpenException()));
                }
            }

            // Локальная.
            static async ValueTask<ClientSideConnection> WaitForConnectionAsync(ValueTask<InnerConnectionResult> t)
            {
                InnerConnectionResult connectionResult = await t.ConfigureAwait(false);
                return connectionResult.ToManagedConnection();
            }
        }

        /// <summary>
        /// Событие — обрыв сокета. Потокобезопасно. Срабатывает только один раз.
        /// </summary>
        private void OnDisconnected(object sender, SocketDisconnectedEventArgs e)
        {
            // volatile.
            _connection = null;
        }

        /// <summary>
        /// Выполнить подключение сокета если еще не подключен.
        /// </summary>
        private ValueTask<InnerConnectionResult> ConnectOrGetExistedConnectionAsync()
        {
            // Копия volatile.
            ClientSideConnection connection = _connection;

            if (connection != null)
            // Есть живое соединение.
            {
                return new ValueTask<InnerConnectionResult>(new InnerConnectionResult(null, null, connection));
            }
            else
            // Подключение отсутствует.
            {
                // Захватить блокировку.
                ValueTask<ChannelLock.Releaser> t = _connectLock.LockAsync();
                if (t.IsCompletedSuccessfully)
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

        private async ValueTask<InnerConnectionResult> WaitForLockAndConnectAsync(ValueTask<ChannelLock.Releaser> t)
        {
            ChannelLock.Releaser releaser = await t.ConfigureAwait(false);
            return await LockAquiredConnectAsync(releaser).ConfigureAwait(false);
        }

        /// <exception cref="WasShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private async ValueTask<InnerConnectionResult> LockAquiredConnectAsync(ChannelLock.Releaser conLock)
        {
            using (conLock)
            {
                // Копия volatile.
                ClientSideConnection connection = _connection;

                if (connection == null)
                {
                    ServiceProvider serviceProvider = ServiceProvider;
                    lock (StateLock)
                    {
                        if (!_disposed)
                        {
                            if (_shutdownRequest != null)
                            {
                                // Нельзя создавать новое подключение если был вызван Stop.
                                return new InnerConnectionResult(null, _shutdownRequest, null);
                            }
                            else
                            {
                                if (serviceProvider == null)
                                {
                                    serviceProvider = _serviceCollection.BuildServiceProvider();
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

                    // Позволить Dispose прервать подключение.
                    Interlocked.Exchange(ref _connectingWs, ws);

                    try
                    {
                        // Обычное подключение Tcp.
                        ReceiveResult receiveResult = await ws.ConnectExAsync(ServerAddress, CancellationToken.None).ConfigureAwait(false);

                        if (Interlocked.Exchange(ref _connectingWs, null) == null)
                        // Stop или Dispose уже вызвали Dispose для ws.
                        {
                            // Предотвратим лишний Dispose.
                            ws = null;

                            lock (StateLock)
                            {
                                if (!_disposed)
                                {
                                    if (_shutdownRequest != null)
                                    {
                                        // Нельзя создавать новое подключение если был вызван Stop.
                                        return new InnerConnectionResult(null, _shutdownRequest, null);
                                    }
                                }
                                else
                                {
                                    // Больше ничего делать не нужно.
                                    throw new ObjectDisposedException(GetType().FullName);
                                }
                            }
                        }

                        if (receiveResult.IsReceivedSuccessfully)
                        // Соединение успешно установлено.
                        {
                            ShutdownRequest stopRequired = null;
                            lock (StateLock)
                            {
                                if (!_disposed)
                                {
                                    connection = new ClientSideConnection(this, ws, serviceProvider, _invokeActions);

                                    // Предотвратить Dispose.
                                    ws = null;

                                    // Скопировать пока мы в блокировке.
                                    stopRequired = _shutdownRequest;

                                    if (stopRequired == null)
                                    {
                                        // Скопируем таск соединения.
                                        Completion = connection.Completion;

                                        // Косвенно устанавливает флаг IsConnected.
                                        _connection = connection;
                                    }
                                }
                                else
                                // Был выполнен Dispose в тот момент когда велась попытка установить соединение.
                                {
                                    throw new ObjectDisposedException(GetType().FullName);
                                }
                            }

                            if (stopRequired == null)
                            // Запроса на остановку сервиса ещё не было.
                            {
                                connection.InitStartThreads();
                                connection.Disconnected += OnDisconnected;
                                return new InnerConnectionResult(receiveResult.SocketError, null, connection);
                            }
                            else
                            // Был запрос на остановку сервиса. 
                            // Он произошел в тот момент когда велась попытка установить соединение.
                            // Это очень редкий случай но мы должны его предусмотреть.
                            {
                                using (connection)
                                {
                                    // Мы обязаны закрыть это соединение.
                                    await connection.InnerShutdownAsync(stopRequired).ConfigureAwait(false);
                                }

                                return new InnerConnectionResult(receiveResult.SocketError, stopRequired, null);
                            }
                        }
                        else
                        // Подключение не удалось.
                        {
                            return new InnerConnectionResult(receiveResult.SocketError, null, null);
                        }
                    }
                    finally
                    {
                        ws?.Dispose();
                    }
                }
                else
                    return new InnerConnectionResult(SocketError.Success, null, connection);
            }
        }

        /// <summary>
        /// Добавляет в IoC контейнер контроллеры из сборки и компилирует контейнер.
        /// </summary>
        private void InnerConfigureIoC(IEnumerable<Type> controllers)
        {
            // Добавим в IoC все контроллеры сборки.
            foreach (Type controllerType in controllers)
                _serviceCollection.AddScoped(controllerType);

            _serviceCollection.AddScoped<GetProxyScope>();
            _serviceCollection.AddScoped(typeof(IProxy<>), typeof(ProxyFactory<>));

            //ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            //return serviceProvider;
        }

        /// <exception cref="ObjectDisposedException"/>
        private void ThrowIfDisposed()
        {
            if (!_disposed)
                return;

            throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// Проверяет установку волатильного свойства <see cref="_shutdownRequest"/>.
        /// </summary>
        /// <exception cref="WasShutdownException"/>
        private void ThrowIfWasShutdown()
        {
            // volatile копия.
            ShutdownRequest shutdownRequired = _shutdownRequest;

            if (shutdownRequired == null)
            {
                return;
            }
            else
            // В этом экземпляре уже был запрос на остановку.
            {
                throw new WasShutdownException(shutdownRequired);
            }
        }

        /// <summary>
        /// Проверяет установку волатильного свойства <see cref="_shutdownRequest"/>.
        /// </summary>
        private bool TryGetShutdownException<T>(out ValueTask<T> exceptionTask)
        {
            // volatile копия.
            ShutdownRequest stopRequired = _shutdownRequest;

            if (stopRequired == null)
            {
                exceptionTask = default;
                return false;
            }
            else
            // В этом экземпляре уже был запрос на остановку.
            {
                exceptionTask = new ValueTask<T>(Task.FromException<T>(new WasShutdownException(stopRequired)));
                return true;
            }
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
