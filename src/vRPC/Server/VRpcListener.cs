using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public sealed partial class VRpcListener : IHostApplicationLifetime, IDisposable
    {
        /// <summary>
        /// Triggered when the application host has fully started.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2213", Justification = "Не требует вызывать Dispose если гарантированно будет вызван Cancel")]
        private readonly CancellationTokenSource _applicationStarted = new();

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown. Shutdown will block until this event completes.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2213", Justification = "Не требует вызывать Dispose если гарантированно будет вызван Cancel")]
        private readonly CancellationTokenSource _applicationStopping = new();
        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown. Shutdown will block until this event completes.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2213", Justification = "Не требует вызывать Dispose если гарантированно будет вызван Cancel")]
        private readonly CancellationTokenSource _applicationStopped = new();

        public CancellationToken ApplicationStarted => _applicationStarted.Token;
        public CancellationToken ApplicationStopping => _applicationStopping.Token;
        public CancellationToken ApplicationStopped => _applicationStopped.Token;

        /// <summary>
        /// Словарь используемый только для чтения, поэтому потокобезопасен.
        /// Хранит все доступные контроллеры. Не учитывает регистр.
        /// </summary>
        internal readonly InvokeActionsDictionary InvokeActions;
        private readonly WebSocketServer _wsServ = new();
        /// <summary>
        /// Доступ через блокировку SyncObj.
        /// </summary>
        private readonly ClientConnections _connections = new();
        private readonly ServiceCollection _serviceCollection = new();
        /// <summary>
        /// Для доступа к <see cref="_stopRequired"/> и <see cref="_started"/>.
        /// </summary>
        private object StartLock => _completionTcs;
        private readonly TaskCompletionSource<bool> _completionTcs = new();
        /// <summary>
        /// <see cref="Task"/> который завершается когда все 
        /// соединения перешли в закрытое состояние и сервис полностью остановлен.
        /// Не бросает исключения.
        /// Возвращает <see langword="true"/> если остановка прошла грациозно.
        /// </summary>
        public Task<bool> Completion => _completionTcs.Task;
        /// <summary>
        /// Единожны меняет состояние на <see langword="true"/>.
        /// </summary>
        private volatile bool _started;
        ///// <summary>
        ///// Коллекция авторизованных пользователей.
        ///// Ключ словаря — UserId авторизованного пользователя.
        ///// </summary>
        //public ConcurrentDictionary<int, UserConnections> Connections { get; } = new ConcurrentDictionary<int, UserConnections>();
        private bool _disposed;
        private ServiceProvider? _serviceProvider;
        /// <summary>
        /// Может быть <see langword="null"/> если не выполнен ConfigureService и сервис ещё не запущен.
        /// </summary>
        public ServiceProvider? ServiceProvider => _serviceProvider;
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
        public event EventHandler<ClientAuthenticatedEventArgs>? ClientAuthenticated;
        public event EventHandler<ClientSignedOutEventArgs>? ClientSignedOut;
        private Action<ServiceProvider>? _configureApp;
        /// <summary>
        /// Не позволяет подключаться новым клиентам. Единожны меняет состояние в момент остановки сервиса.
        /// Также предотвращает повторный запуск сервиса.
        /// </summary>
        private volatile ShutdownRequest? _stopRequired;
        public TimeSpan ClientKeepAliveInterval { get => _wsServ.ClientKeepAliveInterval; set => _wsServ.ClientKeepAliveInterval = value; }
        public TimeSpan ClientReceiveTimeout { get => _wsServ.ClientReceiveTimeout; set => _wsServ.ClientReceiveTimeout = value; }
        public int Port => _wsServ.Port;

        #region ctor

        // ctor.
        public VRpcListener(IPAddress ipAddress) : this (ipAddress, port: 0, Assembly.GetCallingAssembly()) { }

        // ctor.
        public VRpcListener(IPAddress ipAddress, int port) : this(ipAddress, port, Assembly.GetCallingAssembly()) { }

        // ctor.
        internal VRpcListener(IPAddress ipAddress, int port, Assembly controllersAssembly)
        {
            _wsServ.HandshakeTimeout = TimeSpan.FromSeconds(30);
            _wsServ.Bind(new IPEndPoint(ipAddress, port));
            _wsServ.ClientConnected += OnConnected;

            // Контроллеры будем искать в сборке которая вызвала текущую функцию.
            //var controllersAssembly = Assembly.GetCallingAssembly();

            // Сборка с контроллерами не должна быть текущей сборкой.
            Debug.Assert(controllersAssembly != Assembly.GetExecutingAssembly(), "Сборка с контроллерами не должна быть текущей сборкой.");

            // Найти контроллеры в сборке.
            Dictionary<string, Type> controllerTypes = GlobalVars.FindAllControllers(controllersAssembly);

            // Найти все методы в контроллерах.
            InvokeActions = new InvokeActionsDictionary(controllerTypes);

            // Добавим скрытый контроллер для авторизации.
            _serviceCollection.AddScoped(typeof(Controllers.AccountController));

            // Добавить контроллеры в IoC.
            foreach (Type controllerType in controllerTypes.Values)
            {
                _serviceCollection.AddScoped(controllerType);
            }
        }

        #endregion

        #region Public

        /// <summary>
        /// Позволяет настроить IoC контейнер.
        /// Выполняется единожды при инициализации подключения.
        /// </summary>
        /// <param name="configure"></param>
        /// <exception cref="ArgumentNullException"/>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            if (configure == null)
                throw new ArgumentNullException(nameof(configure));

            if (_started)
                ThrowHelper.ThrowVRpcException($"Конфигурация должна осуществляться до начала приёма соединений.");

            configure(_serviceCollection);
            _serviceProvider = BuildServiceCollection();
        }

        /// <exception cref="VRpcException"/>
        public void Configure(Action<ServiceProvider> configureApp)
        {
            if (_started)
                ThrowHelper.ThrowVRpcException($"Конфигурация должна осуществляться до начала приёма соединений.");

            _configureApp = configureApp;
        }

        /// <summary>
        /// Выполняет грациозную остановку сервиса. Блокирует поток не дольше чем задано в <paramref name="disconnectTimeout"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public bool Shutdown(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            return ShutdownAsync(disconnectTimeout, closeDescription).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Начинает остановку сервиса. Не блокирует поток.
        /// Не бросает исключения.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        public void BeginShutdown(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            InnerBeginShutdown(disconnectTimeout, closeDescription);
        }

        /// <summary>
        /// Останавливает сервис и ожидает до полной остановки.
        /// Не бросает исключения.
        /// Эквивалентно <see cref="BeginShutdown(TimeSpan, string)"/> + <see langword="await"/> <see cref="Completion"/>.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина остановки сервиса которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        public Task<bool> ShutdownAsync(TimeSpan disconnectTimeout, string? closeDescription = null)
        {
            InnerBeginShutdown(disconnectTimeout, closeDescription);
            return Completion;
        }

        /// <summary>
        /// Начинает приём подключений и обработку запросов до полной остановки методом Stop.
        /// Эквивалентно вызову метода Start с дальнейшим <see langword="await"/> Completion.
        /// Повторный вызов не допускается.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="VRpcException"/>
        public Task<bool> RunAsync()
        {
            try
            {
                TrySyncStart(shouldThrow: true);
            }
            catch (Exception ex)
            {
                return Task.FromException<bool>(ex);
            }
            return Completion;
        }

        /// <summary>
        /// Начинает приём подключений и обработку запросов до полной остановки методом Stop
        /// или с помощью токена отмены.
        /// Эквивалентно вызову метода Start с дальнейшим <see langword="await"/> Completion.
        /// Повторный вызов не допускается.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="disconnectTimeout">Максимальное время ожидания завершения выполняющихся запросов когда произойдёт остановка сервиса.</param>
        /// <param name="closeDescription">Причина остановки сервиса которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        /// <param name="cancellationToken">Токен служащий для остановки сервиса.</param>
        /// <exception cref="VRpcException"/>
        /// <exception cref="OperationCanceledException"/>
        public Task<bool> RunAsync(TimeSpan disconnectTimeout, string closeDescription, CancellationToken cancellationToken)
        {
            try
            {
                TrySyncStart(shouldThrow: true);
            }
            catch (Exception ex)
            {
                return Task.FromException<bool>(ex);
            }

            // Только один поток зайдёт в этот блок.
            return InnerRunAsync(disconnectTimeout, closeDescription, cancellationToken);
        }

        /// <summary>
        /// Начинает приём новых подключений.
        /// Повторный вызов спровоцирует исключение.
        /// Потокобезопасно.
        /// </summary>
        public void Start()
        {
            TrySyncStart(shouldThrow: false);
        }

        /// <summary>
        /// Возвращает копию коллекции подключенных клиентов.
        /// Потокобезопасно.
        /// </summary>
        public ServerSideConnection[] GetConnections()
        {
            lock (_connections.SyncObj)
            {
                if (_connections.Count > 0)
                    return _connections.ToArray();
            }
            return Array.Empty<ServerSideConnection>();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _wsServ.Dispose();
                _serviceProvider?.Dispose();
                DisposeAllConnections();
            }
        }

        #endregion

        #region Internal

        internal void OnConnectionAuthenticated(ServerSideConnection connection, ClaimsPrincipal user)
        {
            Debug.Assert(user.Identity != null);
            Debug.Assert(user.Identity.IsAuthenticated);

            ClientAuthenticated?.Invoke(this, new ClientAuthenticatedEventArgs(connection, user));
        }

        internal void OnUserSignedOut(ServerSideConnection connection, ClaimsPrincipal user)
        {
            Debug.Assert(user.Identity?.IsAuthenticated == true);

            ClientSignedOut?.Invoke(this, new ClientSignedOutEventArgs(connection, user));
        }

        /// <summary>
        /// Возвращает копию коллекции подключенных клиентов кроме <paramref name="exceptCon"/>.
        /// Потокобезопасно.
        /// </summary>
        internal ServerSideConnection[] GetConnectionsExcept(ServerSideConnection exceptCon)
        {
            lock (_connections.SyncObj)
            {
                int selfIndex = _connections.IndexOf(exceptCon);
                if (selfIndex != -1)
                {
                    if (_connections.Count > 1)
                    {
                        var ar = new ServerSideConnection[_connections.Count - 1];
                        for (int i = 0; i < _connections.Count; i++)
                        {
                            if (i != selfIndex)
                            {
                                ar[i] = _connections[i];
                            }
                        }
                        return ar;
                    }
                }
                else
                {
                    return _connections.ToArray();
                }
            }
            return Array.Empty<ServerSideConnection>();
        }

        #endregion

        #region Private

        private ServiceProvider BuildServiceCollection()
        {
            _serviceCollection.AddScoped<RequestContextScope>();
            _serviceCollection.AddScoped(typeof(IProxy<>), typeof(ProxyFactory<>));
            _serviceCollection.AddSingleton<IHostApplicationLifetime>(this);

            return _serviceCollection.BuildServiceProvider();
        }

        /// <summary>
        /// Начинает остановку сервера и взводит <see cref="Completion"/> не дольше чем указано в <paramref name="disconnectTimeout"/>.
        /// </summary>
        private async void InnerBeginShutdown(TimeSpan disconnectTimeout, string? closeDescription)
        {
            ShutdownRequest? stopRequired;
            lock (StartLock)
            {
                // Копия volatile.
                stopRequired = _stopRequired;

                if (stopRequired == null)
                {
                    stopRequired = new ShutdownRequest(disconnectTimeout, closeDescription);

                    // Это volatile свойство нужно установить перед 
                    // остановкой WebSocketServer, оно не допустит новые соединения.
                    _stopRequired = stopRequired;

                    // Прекратить приём новых соединений.
                    _wsServ.Dispose();
                }
                else
                // Остановка уже была инициализирована.
                {
                    return;
                }
            }

            // Только один поток выполнит дальнейший блок.

            // Выполним здесь - после блокировки.
            _applicationStopping.Cancel();

            // Необходимо закрыть все соединения.
            ServerSideConnection[] activeConnections = Array.Empty<ServerSideConnection>();
            lock (_connections.SyncObj)
            {
                if (_connections.Count > 0)
                {
                    // Теперь мы обязаны сделать Dispose этим подключениям.
                    activeConnections = _connections.ToArray();
                    _connections.Clear();
                }
            }

            if (activeConnections.Length > 0)
            {
                var cliConStopTasks = new Task<CloseReason>[activeConnections.Length];

                // Грациозно останавливаем соединения.
                for (int i = 0; i < activeConnections.Length; i++)
                {
                    ServerSideConnection clientConnection = activeConnections[i];

                    // Прекращаем принимать запросы.
                    // Не бросает исключений.
                    cliConStopTasks[i] = clientConnection.InnerShutdownAsync(stopRequired);
                }

                CloseReason[] allConnTask = await Task.WhenAll(cliConStopTasks).ConfigureAwait(false);

                // Грациозная остановка сервера это когда все клиенты
                // отключились до достижения таймаута.
                bool gracefully = allConnTask.All(x => x.Gracifully);

                // Установить Completion.
                _completionTcs.TrySetResult(gracefully);
            }
            else
            // К серверу не было установлено ни одного соединения.
            {
                // Установить Completion.
                _completionTcs.TrySetResult(true);
            }

            _applicationStopped.Cancel();
        }

        /// <exception cref="OperationCanceledException"/>
        private async Task<bool> InnerRunAsync(TimeSpan disconnectTimeout, string closeDescription, CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(s => BeginShutdown(disconnectTimeout, closeDescription), false))
            {
                bool graceful = await Completion.ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                return graceful;
            }
        }

        /// <summary>
        /// Потокобезопасно запускает сервер. Только первый поток получит True.
        /// </summary>
        /// <exception cref="VRpcException"/>
        private void TrySyncStart(bool shouldThrow)
        {
            bool success;
            lock (StartLock)
            {
                success = InnerTryStart(shouldThrow);
            }

            if (success)
            {
                _applicationStarted.Cancel();
            }
        }

        /// <summary>
        /// Предотвращает повторный запуск сервера.
        /// </summary>
        /// <exception cref="VRpcException"/>
        /// <exception cref="VRpcShutdownException"/>
        private bool InnerTryStart(bool shouldThrow)
        {
            Debug.Assert(Monitor.IsEntered(StartLock));

            if (_stopRequired == null)
            {
                if (!_started)
                {
                    _started = true;
                    if (_serviceProvider == null)
                    {
                        _serviceProvider = BuildServiceCollection();
                        _configureApp?.Invoke(_serviceProvider);
                    }

                    // Начать принимать подключения.
                    _wsServ.StartAccept();

                    // Сервер запущен.
                    return true;
                }
                else
                {
                    if (shouldThrow)
                        ThrowHelper.ThrowVRpcException("A server is already running");
                }
            }
            else
            {
                if (shouldThrow)
                    ThrowHelper.ThrowWasShutdownException(_stopRequired);
            }

            // Сервер уже запущен или находится в остановленном состоянии.
            return false;
        }

        private void OnConnected(object? sender, DanilovSoft.WebSockets.ClientConnectedEventArgs e)
        {
            // Возможно сервер находится в режиме остановки.
            ServerSideConnection? connection;
            lock (_connections.SyncObj)
            {
                if (_stopRequired == null) // volatile.
                {
                    Debug.Assert(_serviceProvider != null, "На этом этапе контейнер должен быть инициализирован");

                    // Создать контекст для текущего подключения.
                    connection = new ServerSideConnection(e.WebSocket, _serviceProvider, this);

                    _connections.Add(connection);
                }
                else
                // Был запрос на остановку сервера но кто-то успел подключиться.
                {
                    // Начать закрытие сокета.
                    CloseWebSocket(e.WebSocket, _stopRequired.CloseDescription);

                    // Игнорируем это подключение.
                    connection = null;
                }
            }

            if (connection != null)
            {
                // Сервер разрешил установку этого соединения, можно начать чтение.
                connection.StartReceiveSendLoop();

                // Сначала нужно запустить чтение, а потом вызвать событие.
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(connection));

                // Состояние гонки здесь отсутствует.
                // Событие гарантирует что обрыв пропущен не будет.
                connection.Disconnected += Context_Disconnected;
            }
        }

        private static async void CloseWebSocket(ManagedWebSocket ws, string? closeDescription)
        {
            using (ws)
            {
                try
                {
                    await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        closeDescription, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }

        private void Context_Disconnected(object? sender, SocketDisconnectedEventArgs e)
        {
            var context = sender as ServerSideConnection;
            Debug.Assert(context != null);

            lock (_connections.SyncObj)
            {
                _connections.Remove(context);
            }
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(context, e.DisconnectReason));
        }

        private void DisposeAllConnections()
        {
            ServerSideConnection[] connections;

            lock (_connections.SyncObj)
            {
                connections = _connections.Count > 0
                    ? _connections.ToArray()
                    : Array.Empty<ServerSideConnection>();

                _connections.Clear();
            }

            for (int i = 0; i < connections.Length; i++)
            {
                connections[i].Dispose();
            }
        }

        #endregion
    }
}
