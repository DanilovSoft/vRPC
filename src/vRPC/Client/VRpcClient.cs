using DanilovSoft.vRPC.Decorator;
using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static DanilovSoft.vRPC.ThrowHelper;

namespace DanilovSoft.vRPC
{

    /// <summary>
    /// Контекст клиентского соединения.
    /// </summary>
    [DebuggerDisplay(@"\{{DebugDisplay,nq}\}")]
    public sealed class VRpcClient : IDisposable/*, IGetProxy*/
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay
        {
            get
            {
                var state = State;
                if (state == VRpcState.Open && IsAuthenticated)
                {
                    return $"{state}, Authenticated";
                }
                return state.ToString();
            }
        }
        /// <summary>
        /// Используется для синхронизации установки соединения.
        /// </summary>
        private readonly AsyncLock _connectLock;
        /// <summary>
        /// Адрес для подключения к серверу.
        /// </summary>
        private readonly InvokeActionsDictionary _invokeActions;
        private readonly ProxyCache _proxyCache;
        private readonly ServiceCollection _serviceCollection = new();
        private bool IsAutoConnectAllowed { get; }
        public Uri ServerAddress { get; private set; }
        private ApplicationBuilder? _appBuilder;
        public ServiceProvider? ServiceProvider { get; private set; }
        private Action<ApplicationBuilder>? _configureApp;
        private Func<AccessToken>? _autoAuthentication;

        /// <summary>
        /// Устанавливается в блокировке <see cref="StateLock"/>.
        /// Устанавливается в Null при обрыве соединения.
        /// </summary>
        /// <remarks><see langword="volatile"/></remarks>
        private volatile ClientSideConnection? _connection;
        /// <summary>
        /// Активное соединение. Может быть Null если соединение отсутствует.
        /// </summary>
        public ClientSideConnection? Connection => _connection;

        private Task<CloseReason>? _completion;
        /// <summary>
        /// Завершается если подключение разорвано.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion => _completion ?? CloseReason.NoConnectionCompletion;
        public VRpcState State
        {
            get
            {
                if (_shutdownRequest != null)
                    return VRpcState.ShutdownRequest;

                return _connection != null ? VRpcState.Open : VRpcState.Closed;
            }
        }
        /// <summary>
        /// Запись через блокировку <see cref="StateLock"/>.
        /// </summary>
        /// <remarks><see langword="volatile"/> служит для публичного доступа.</remarks>
        private volatile ShutdownRequest? _shutdownRequest;
        /// <summary>
        /// Если был начат запрос на остновку, то это свойство будет содержать переданную причину остановки.
        /// Является <see langword="volatile"/>.
        /// </summary>
        public ShutdownRequest? StopRequiredState => _shutdownRequest;
        private bool _disposed;
        /// <summary>
        /// Для доступа к <see cref="_disposed"/> и <see cref="_shutdownRequest"/>.
        /// </summary>
        private object StateLock => _proxyCache;
        /// <summary>
        /// Используется только что-бы аварийно прервать подключение через Dispose.
        /// </summary>
        private ClientWebSocket? _connectingWs;
        /// <summary>
        /// True если соединение прошло аутентификацию на сервере.
        /// </summary>
        public bool IsAuthenticated => _connection?.IsAuthenticated ?? false;
        public event EventHandler<ConnectedEventArgs>? Connected;
        public System.Net.EndPoint? LocalEndPoint => _connection?.LocalEndPoint;
        public System.Net.EndPoint? RemoteEndPoint => _connection?.RemoteEndPoint;

        #region Public ctor

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        /// <param name="allowAutoConnect">Разрешено ли интерфейсам самостоятельно устанавливать и повторно переподключаться к серверу.</param>
        public VRpcClient(Uri serverAddress, bool allowAutoConnect) 
            : this(Assembly.GetCallingAssembly(), serverAddress, allowAutoConnect)
        {

        }

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        /// <param name="allowAutoConnect">Разрешено ли интерфейсам самостоятельно устанавливать и повторно переподключаться к серверу.</param>
        public VRpcClient(string host, int port, bool ssl, bool allowAutoConnect) 
            : this(Assembly.GetCallingAssembly(), new Uri($"{(ssl ? "wss" : "ws")}://{host}:{port}"), allowAutoConnect)
        {
            
        }

        #endregion

        #region Ctor

        // ctor.
        /// <summary>
        /// Конструктор клиента.
        /// </summary>
        /// <param name="controllersAssembly">Сборка в которой осуществляется поиск контроллеров.</param>
        /// <param name="serverAddress">Адрес сервера.</param>
        private VRpcClient(Assembly controllersAssembly, Uri serverAddress, bool allowAutoConnect)
        {
            Debug.Assert(controllersAssembly != Assembly.GetExecutingAssembly());

            // Найти все контроллеры в вызывающей сборке.
            Dictionary<string, Type> controllerTypes = GlobalVars.FindAllControllers(controllersAssembly);

            // Словарь с методами контроллеров.
            _invokeActions = new InvokeActionsDictionary(controllerTypes);
            ServerAddress = serverAddress;
            _connectLock = new AsyncLock();
            IsAutoConnectAllowed = allowAutoConnect;
            _proxyCache = new ProxyCache();

            InnerConfigureIoC(controllerTypes.Values);
        }

        #endregion

        #region Public

        /// <summary>
        /// Позволяет настроить IoC контейнер.
        /// Выполняется единожды при инициализации подключения.
        /// </summary>
        /// <exception cref="VRpcException"/>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            ThrowIfDisposed();

            if (ServiceProvider != null)
                ThrowVRpcException("Service already configured.");

            configure(_serviceCollection);
            ServiceProvider = _serviceCollection.BuildServiceProvider();
        }

        /// <exception cref="VRpcException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void Configure(Action<ApplicationBuilder> configureApp)
        {
            if (configureApp == null)
                ThrowArgumentNullException(nameof(configureApp));

            ThrowIfDisposed();

            if (_configureApp != null)
                ThrowVRpcException("RpcClient already configured.");

            _configureApp = configureApp;
        }

        /// <exception cref="VRpcException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void ConfigureAutoAuthentication(Func<AccessToken> configure)
        {
            if (configure == null)
                ThrowArgumentNullException(nameof(configure));

            ThrowIfDisposed();

            if (_autoAuthentication != null)
                ThrowVRpcException("Auto authentication already configured.");

            _autoAuthentication = configure;
        }

        /// <summary>
        /// Блокирует поток до завершения <see cref="Completion"/>.
        /// </summary>
        public CloseReason WaitCompletion()
        {
            return Completion.GetAwaiter().GetResult();
        }

        #region Public Connect

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного подключения.
        /// </summary>
        /// <remarks>Потокобезопасно.</remarks>
        /// <exception cref="VRpcConnectException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        public void Connect()
        {
            ConnectAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// </summary>
        /// <remarks>Потокобезопасно.</remarks>
        /// <exception cref="VRpcConnectException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        public async Task ConnectAsync()
        {
            ConnectResult connectResult = await ConnectExAsync().ConfigureAwait(false);
            
            switch (connectResult.State)
            {
                case ConnectionState.Connected:
                    return;
                case ConnectionState.SocketError:
                    {
                        Debug.Assert(connectResult.SocketError != null);

                        ThrowConnectException($"Unable to connect to the remote server. Error: {(int)connectResult.SocketError}",
                            innerException: connectResult.SocketError.Value.ToException());

                        break;
                    }
                case ConnectionState.ShutdownRequest:
                    {
                        Debug.Assert(connectResult.ShutdownRequest != null);
                        ThrowHelper.ThrowException(connectResult.ShutdownRequest.ToException());
                        break;
                    }
            }
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Помимо кода возврата может бросить исключение типа <see cref="VRpcConnectException"/>.
        /// </summary>
        /// <remarks>Потокобезопасно.</remarks>
        /// <exception cref="VRpcConnectException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        public ConnectResult ConnectEx()
        {
            return ConnectExAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Помимо кода возврата может бросить исключение типа <see cref="VRpcConnectException"/>.
        /// </summary>
        /// <remarks>Потокобезопасно.</remarks>
        /// <exception cref="VRpcConnectException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Task<ConnectResult> ConnectExAsync()
        {
            if (IsDisposed(out var disEx))
                return Task.FromException<ConnectResult>(disEx);

            if (WasShutdown(out var shutdEx))
                return Task.FromException<ConnectResult>(shutdEx);

            ValueTask<InnerConnectionResult> t = ConnectOrGetExistedConnectionAsync(default);
            if(t.IsCompleted)
            {
                InnerConnectionResult conRes;
                try
                {
                    // Может бросить исключение.
                    conRes = t.Result;
                }
                catch (Exception ex)
                {
                    return Task.FromException<ConnectResult>(ex);
                }
                return Task.FromResult(conRes.ToPublicConnectResult());
            }
            else
            {
                return WaitForConnectAsync(t);
            }

            // Локальная.
            static async Task<ConnectResult> WaitForConnectAsync(ValueTask<InnerConnectionResult> t)
            {
                InnerConnectionResult conRes;
                try
                {
                    conRes = await t.ConfigureAwait(false);
                }
                catch (SocketException ex)
                {
                    ThrowConnectException($"Unable to connect to the remote server. ErrorCode: {ex.ErrorCode}", ex);
                    return default;
                }
                catch (System.Net.WebSockets.WebSocketException ex)
                {
                    ThrowConnectException($"Unable to connect to the remote server. ErrorCode: {ex.ErrorCode}", ex);
                    return default;
                }
                catch (HttpHandshakeException ex)
                {
                    ThrowConnectException($"Unable to connect to the remote server due to handshake error", ex);
                    return default;
                }
                return conRes.ToPublicConnectResult();
            }
        }
        #endregion

        /// <summary>
        /// Выполняет аутентификацию текущего соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        public void SignIn(AccessToken accessToken)
        {
            SignInAsync(accessToken).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Выполняет аутентификацию текущего соединения.
        /// </summary>
        /// <param name="accessToken">Аутентификационный токен передаваемый серверу.</param>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        public Task SignInAsync(AccessToken accessToken)
        {
            if (!accessToken.AccessTokenIsValid(nameof(accessToken), out var argEx))
                return Task.FromException(argEx);

            if (IsDisposed(out var disEx))
                return Task.FromException(disEx);

            if (WasShutdown(out var shutdEx))
                return Task.FromException(shutdEx);

            // Начать соединение или взять существующее.
            ValueTask<ClientSideConnection> connectionTask = GetOrOpenConnection(accessToken);

            if (connectionTask.IsCompleted)
            {
                ClientSideConnection connection;
                try
                {
                    // Может бросить исключение.
                    connection = connectionTask.Result;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
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
        /// <exception cref="ObjectDisposedException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        public void SignOut()
        {
            SignOutAsync().GetAwaiter().GetResult();
        }

        /// <exception cref="VRpcConnectionNotOpenException"/>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Task SignOutAsync()
        {
            if (IsDisposed(out var ex))
                return Task.FromException(ex);

            if (WasShutdown(out var ex2))
                return Task.FromException(ex2);

            // Копия volatile.
            ClientSideConnection? connection = _connection;

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
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// Полученный экземпляр можно привести к типу <see cref="ClientInterfaceProxy"/>.
        /// Метод является шорткатом для <see cref="GetProxyDecorator"/>
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public T GetProxy<T>() where T : class
        {
            var proxy = GetProxyDecorator<T>().Proxy;
            Debug.Assert(proxy != null);
            return proxy;
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

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует поток не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// </summary>
        /// <remarks>Потокобезопасно.</remarks>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public CloseReason Shutdown(TimeSpan disconnectTimeout, string? closeDescription = null)
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
        public void BeginShutdown(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            _ = PrivateShutdownAsync(disconnectTimeout, closeDescription);
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public Task<CloseReason> ShutdownAsync(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            return PrivateShutdownAsync(disconnectTimeout, closeDescription);
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
                    Connected = null;
                }
            }
        }

        #endregion

        #region Internal

        /// <summary>
        /// Возвращает существующее подключение или создаёт новое если это разрешает свойство <see cref="IsAutoConnectAllowed"/>.
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        internal ValueTask<ClientSideConnection> GetOrOpenConnection(AccessToken accessToken)
        {
            // Копия volatile.
            ClientSideConnection? connection = _connection;

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
                        ValueTask<InnerConnectionResult> t = ConnectOrGetExistedConnectionAsync(accessToken);
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
                    // Копия volatile.
                    ShutdownRequest? shutdownRequest = _shutdownRequest;
                    if (shutdownRequest != null)
                    {
                        return new(Task.FromException<ClientSideConnection>(shutdownRequest.ToException()));
                    }
                    else
                        return new(Task.FromException<ClientSideConnection>(new VRpcConnectionNotOpenException()));
                }
            }

            // Локальная.
            static async ValueTask<ClientSideConnection> WaitForConnectionAsync(ValueTask<InnerConnectionResult> t)
            {
                InnerConnectionResult connectionResult = await t.ConfigureAwait(false);
                return connectionResult.ToManagedConnection();
            }
        }

        // Когда выполняют вызов метода через интерфейс.
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        internal Task<TResult> OnClientMethodCall<TResult>(RequestMethodMeta method, object[] args)
        {
            Debug.Assert(method.ReturnType == typeof(TResult));
            Debug.Assert(!method.IsNotificationRequest);

            // Начать соединение или взять существующее.
            ValueTask<ClientSideConnection> connectionTask = GetOrOpenConnection(accessToken: default);

            return RpcManagedConnection.OnClientRequestCall<TResult>(connectionTask, method, args);
        }

        /// <summary>
        /// Когда выполняют вызов метода через интерфейс.
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="VRpcConnectionNotOpenException"/>
        internal ValueTask OnClientNotificationCall(RequestMethodMeta methodMeta, object[] args)
        {
            // Начать соединение или взять существующее.
            ValueTask<ClientSideConnection> connectionTask = GetOrOpenConnection(default);

            return RpcManagedConnection.OnClientNotificationCall(connectionTask, methodMeta, args);
        }

        #endregion

        #region Private

        private async Task<CloseReason> PrivateShutdownAsync(TimeSpan disconnectTimeout, string? closeDescription)
        {
            bool created;
            ShutdownRequest? stopRequired;
            ClientSideConnection? connection;
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
        /// Событие — обрыв сокета. Потокобезопасно. Срабатывает только один раз.
        /// </summary>
        private void OnDisconnected(object? sender, SocketDisconnectedEventArgs e)
        {
            // volatile.
            _connection = null;

            // Отпишем отключенный экземпляр.
            e.Connection.Disconnected -= OnDisconnected;
        }

        /// <summary>
        /// Выполнить подключение сокета если еще не подключен.
        /// </summary>
        /// <exception cref="ObjectDisposedException"/>
        private ValueTask<InnerConnectionResult> ConnectOrGetExistedConnectionAsync(AccessToken accessToken)
        {
            // Копия volatile.
            ClientSideConnection? connection = _connection;

            if (connection != null)
            // Есть живое соединение.
            {
                return new(InnerConnectionResult.FromExistingConnection(connection));
            }
            else
            // Подключение отсутствует.
            {
                // Захватить блокировку.
                ValueTask<AsyncLock.Releaser> t = _connectLock.LockAsync();
                if (t.IsCompletedSuccessfully)
                {
                    AsyncLock.Releaser releaser = t.Result;
                    return LockAquiredConnectAsync(releaser, accessToken);
                }
                else
                {
                    return WaitForLockAndConnectAsync(t, accessToken);
                }
            }

            async ValueTask<InnerConnectionResult> WaitForLockAndConnectAsync(ValueTask<AsyncLock.Releaser> t, AccessToken accessToken)
            {
                AsyncLock.Releaser releaser = await t.ConfigureAwait(false);
                return await LockAquiredConnectAsync(releaser, accessToken).ConfigureAwait(false);
            }
        }

        /// <exception cref="VRpcShutdownException"/>
        /// <exception cref="ObjectDisposedException"/>
        private async ValueTask<InnerConnectionResult> LockAquiredConnectAsync(AsyncLock.Releaser conLock, AccessToken accessToken)
        {
            InnerConnectionResult conResult;
            try
            {
                conResult = await LockAquiredConnectAsync(accessToken).ConfigureAwait(false);
            }
            finally
            {
                conLock.Dispose();
            }

            // Только один поток получит соединение с этим флагом.
            if (conResult.NewConnectionCreated)
            {
                Debug.Assert(conResult.Connection != null);
                Connected?.Invoke(this, new ConnectedEventArgs(conResult.Connection));
            }
            return conResult;
        }

        /// <exception cref="ObjectDisposedException"/>
        private async ValueTask<InnerConnectionResult> LockAquiredConnectAsync(AccessToken accessToken)
        {
            // Копия volatile.
            ClientSideConnection? connection = _connection;

            if (connection == null)
            {
                ServiceProvider? serviceProvider = ServiceProvider;
                lock (StateLock)
                {
                    if (!_disposed)
                    {
                        // Пока в блокировке можно безопасно трогать свойство _shutdownRequest.
                        if (_shutdownRequest == null)
                        {
                            if (serviceProvider == null)
                            {
                                serviceProvider = _serviceCollection.BuildServiceProvider();
                                ServiceProvider = serviceProvider;
                            }
                        }
                        else
                        {
                            // Нельзя создавать новое подключение если был вызван Stop.
                            return InnerConnectionResult.FromShutdownRequest(_shutdownRequest);
                        }
                    }
                    else
                    {
                        ThrowObjectDisposedException(GetType().FullName);
                    }
                }

                _appBuilder = new ApplicationBuilder(serviceProvider);
                _configureApp?.Invoke(_appBuilder);

                // Новый сокет.
                var ws = new ClientWebSocket();
                ClientWebSocket? toDispose = ws;

                ws.Options.KeepAliveInterval = _appBuilder.KeepAliveInterval;
                ws.Options.ReceiveTimeout = _appBuilder.ReceiveTimeout;

                // Позволить Dispose прервать подключение.
                Interlocked.Exchange(ref _connectingWs, ws);

                try
                {
                    ReceiveResult wsReceiveResult;
                    try
                    {
                        // Обычное подключение Web-Socket.
                        wsReceiveResult = await ws.ConnectExAsync(ServerAddress, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) when (_shutdownRequest != null)
                    {
                        return InnerConnectionResult.FromShutdownRequest(_shutdownRequest);
                    }
                    catch (Exception ex)
                    {
                        throw new VRpcConnectException("Unable to connect to the remote server.", ex);
                    }

                    if (Interlocked.Exchange<ClientWebSocket?>(ref _connectingWs, null) == null)
                    // Другой поток уничтожил наш web-socket.
                    {
                        // Предотвратим лишний Dispose.
                        toDispose = null;

                        lock (StateLock)
                        {
                            if (!_disposed)
                            {
                                if (_shutdownRequest != null)
                                // Другой поток вызвал Shutdown.
                                {
                                    return InnerConnectionResult.FromShutdownRequest(_shutdownRequest);
                                }
                            }
                            else
                            // Другой поток вызвал Dispose.
                            {
                                // Больше ничего делать не нужно.
                                ThrowObjectDisposedException(GetType().FullName);
                            }
                        }
                    }

                    if (wsReceiveResult.IsReceivedSuccessfully)
                    // Соединение успешно установлено.
                    {
                        ShutdownRequest? stopRequired = null;
                        lock (StateLock)
                        {
                            if (!_disposed)
                            {
                                Debug.Assert(ws.ManagedWebSocket != null);
                                connection = new ClientSideConnection(this, ws.ManagedWebSocket, serviceProvider, _invokeActions);

                                // Предотвратить Dispose.
                                toDispose = null;

                                // Скопировать пока мы в блокировке.
                                stopRequired = _shutdownRequest;

                                if (stopRequired == null)
                                {
                                    // Скопируем таск соединения.
                                    _completion = connection.Completion;

                                    // Косвенно устанавливает флаг IsConnected.
                                    _connection = connection;
                                }
                            }
                            else
                            // Был выполнен Dispose в тот момент когда велась попытка установить соединение.
                            {
                                ThrowObjectDisposedException(GetType().FullName);
                            }
                        }

                        if (stopRequired == null)
                        // Запроса на остановку сервиса ещё не было.
                        {
                            connection.StartReceiveSendLoop();
                            connection.Disconnected += OnDisconnected;

                            if (accessToken == (default))
                            {
                                // Запросить токен у пользователя.
                                AccessToken autoAccessToken = _autoAuthentication?.Invoke() ?? default;
                                if (accessToken != default)
                                {
                                    await connection.PrivateSignInAsync(accessToken).ConfigureAwait(false);
                                }
                            }
                            else
                            // Приоритет у токена переданный как параметр — это явная SignIn операция.
                            {
                                await connection.PrivateSignInAsync(accessToken).ConfigureAwait(false);
                            }

                            // Успешно подключились.
                            return InnerConnectionResult.FromNewConnection(connection);
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

                            return InnerConnectionResult.FromShutdownRequest(stopRequired);
                        }
                    }
                    else
                    // Подключение не удалось.
                    {
                        return InnerConnectionResult.FromConnectionError(wsReceiveResult.SocketError);
                    }
                }
                finally
                {
                    toDispose?.Dispose();
                }
            }
            else
            // Подключать сокет не нужно — есть живое соединение.
            {
                return InnerConnectionResult.FromExistingConnection(connection);
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

            _serviceCollection.AddScoped<RequestContextScope>();
            _serviceCollection.AddScoped(typeof(IProxy<>), typeof(ProxyFactory<>));

            //ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();
            //return serviceProvider;
        }

        /// <exception cref="ObjectDisposedException"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (!_disposed)
            {
                return;
            }
            else
            {
                ThrowObjectDisposedException(GetType().FullName);
            }
        }

        private bool IsDisposed([NotNullWhen(true)] out ObjectDisposedException? exception)
        {
            if (!_disposed)
            {
                exception = null;
                return false;
            }
            else
            {
                exception = new ObjectDisposedException(GetType().FullName);
                return true;
            }
        }

        /// <summary>
        /// Проверяет установку волатильного свойства <see cref="_shutdownRequest"/>.
        /// </summary>
        /// <exception cref="VRpcShutdownException"/>
        private void ThrowIfWasShutdown()
        {
            // volatile копия.
            ShutdownRequest? shutdownRequired = _shutdownRequest;

            if (shutdownRequired == null)
            {
                return;
            }
            else
            // В этом экземпляре уже был запрос на остановку.
            {
                ThrowWasShutdownException(shutdownRequired);
            }
        }

        private bool WasShutdown([NotNullWhen(true)] out VRpcShutdownException? exception)
        {
            // volatile копия.
            ShutdownRequest? shutdownRequired = _shutdownRequest;

            if (shutdownRequired == null)
            {
                exception = null;
                return false;
            }
            else
            // В этом экземпляре уже был запрос на остановку.
            {
                exception = new VRpcShutdownException(shutdownRequired);
                return true;
            }
        }

        /// <summary>
        /// Проверяет установку волатильного свойства <see cref="_shutdownRequest"/>.
        /// </summary>
        private bool TryGetShutdownException<T>(out ValueTask<T> exceptionTask)
        {
            // volatile копия.
            ShutdownRequest? stopRequired = _shutdownRequest;

            if (stopRequired == null)
            {
                exceptionTask = default;
                return false;
            }
            else
            // В этом экземпляре уже был запрос на остановку.
            {
                exceptionTask = new(Task.FromException<T>(new VRpcShutdownException(stopRequired)));
                return true;
            }
        }

        #endregion

        //private sealed class DebugProxy
        //{
        //    private readonly VRpcClient _self;
        //    public Uri ServerAddress => _self.ServerAddress;
        //    public ClientSideConnection? Connection => _self.Connection;

        //    public DebugProxy(VRpcClient self)
        //    {
        //        _self = self;
        //    }
        //}
    }
}
