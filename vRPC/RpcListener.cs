using DanilovSoft.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    public sealed class RpcListener : IDisposable
    {
        /// <summary>
        /// Словарь используемый только для чтения, поэтому потокобезопасен.
        /// Хранит все доступные контроллеры. Не учитывает регистр.
        /// </summary>
        internal readonly ControllerActionsDictionary Controllers;
        private readonly WebSocketServer _wsServ = new WebSocketServer();
        /// <summary>
        /// Доступ через блокировку SyncObj.
        /// </summary>
        private readonly ClientConnections _connections = new ClientConnections();
        private readonly ServiceCollection _serviceCollection = new ServiceCollection();
        /// <summary>
        /// Для доступа к <see cref="_stopRequired"/> и <see cref="_started"/>.
        /// </summary>
        private object StartLock => _completionTcs;
        private readonly TaskCompletionSource<bool> _completionTcs = new TaskCompletionSource<bool>();
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
        private bool _started;
        ///// <summary>
        ///// Коллекция авторизованных пользователей.
        ///// Ключ словаря — UserId авторизованного пользователя.
        ///// </summary>
        //public ConcurrentDictionary<int, UserConnections> Connections { get; } = new ConcurrentDictionary<int, UserConnections>();
        private bool _disposed;
        private ServiceProvider _serviceProvider;
        /// <summary>
        /// Может быть <see langword="null"/> если не выполнен ConfigureService и сервис ещё не запущен.
        /// </summary>
        public ServiceProvider ServiceProvider => _serviceProvider;
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;
        private Action<ServiceProvider> _configureApp;
        /// <summary>
        /// Не позволяет подключаться новым клиентам. Единожны меняет состояние.
        /// </summary>
        private volatile StopRequired _stopRequired;
        public TimeSpan ClientKeepAliveInterval { get => _wsServ.ClientKeepAliveInterval; set => _wsServ.ClientKeepAliveInterval = value; }
        public TimeSpan ClientReceiveTimeout { get => _wsServ.ClientReceiveTimeout; set => _wsServ.ClientReceiveTimeout = value; }

        static RpcListener()
        {
            Warmup.DoWarmup();
        }

        // ctor.
        public RpcListener(IPAddress ipAddress, int port)
        {
            _wsServ.HandshakeTimeout = TimeSpan.FromSeconds(30);
            _wsServ.Bind(new IPEndPoint(ipAddress, port));
            _wsServ.ClientConnected += Listener_OnConnected;

            // Контроллеры будем искать в сборке которая вызвала текущую функцию.
            var controllersAssembly = Assembly.GetCallingAssembly();

            // Сборка с контроллерами не должна быть текущей сборкой.
            Debug.Assert(controllersAssembly != Assembly.GetExecutingAssembly());

            // Найти контроллеры в сборке.
            Controllers = new ControllerActionsDictionary(GlobalVars.FindAllControllers(controllersAssembly));

            // Добавить контроллеры в IoC.
            foreach (Type controllerType in Controllers.Controllers.Values)
            {
                _serviceCollection.AddScoped(controllerType);
            }
        }

        /// <summary>
        /// Позволяет настроить IoC контейнер.
        /// Выполняется единожды при инициализации подключения.
        /// </summary>
        /// <param name="configure"></param>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            configure(_serviceCollection);
            _serviceProvider = _serviceCollection.BuildServiceProvider();
        }

        public void Configure(Action<ServiceProvider> configureApp)
        {
            _configureApp = configureApp;
        }

        /// <summary>
        /// Начинает остановку сервиса. Не блокирует поток.
        /// Не бросает исключения.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        public void Stop(TimeSpan timeout, string closeDescription = null)
        {
            BeginStop(timeout, closeDescription);
        }

        /// <summary>
        /// Останавливает сервис и ожидает до полной остановки.
        /// Не бросает исключения.
        /// Эквивалентно <see cref="Stop(TimeSpan)"/> + <see langword="await"/> <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        public Task<bool> StopAsync(TimeSpan timeout, string closeDescription = null)
        {
            BeginStop(timeout, closeDescription);
            return Completion;
        }

        private async void BeginStop(TimeSpan timeout, string closeDescription)
        {
            StopRequired stopRequired;
            lock (StartLock)
            {
                stopRequired = _stopRequired;
                if (stopRequired == null)
                {
                    stopRequired = new StopRequired(timeout, closeDescription);
                    _stopRequired = stopRequired;

                    // Прекратить приём новых соединений.
                    _wsServ.Dispose();
                }
            }

            // Необходимо закрыть все соединения.

            var activeConnections = Array.Empty<ServerSideConnection>();
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
                var tasks = new Task<CloseReason>[activeConnections.Length];

                // Грациозно останавливаем соединения.
                for (int i = 0; i < activeConnections.Length; i++)
                {
                    var con = activeConnections[i];

                    // Прекращаем принимать запросы.
                    tasks[i] = con.StopAsync(stopRequired);
                }

                var allConnTask = await Task.WhenAll(tasks).ConfigureAwait(false);

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
        }

        /// <summary>
        /// Начинает приём подключений и обработку запросов до полной остановки методом Stop.
        /// Эквивалентно вызову Start + <see langword="await"/> Completion.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="RpcListenerException"/>
        public Task<bool> RunAsync()
        {
            lock (StartLock)
            {
                InnerStart();

                return Completion;
            }
        }

        /// <summary>
        /// Начинает приём новых подключений.
        /// Повторный вызов спровоцирует исключение.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="RpcListenerException"/>
        public void Start()
        {
            lock (StartLock)
            {
                InnerStart();
            }
        }

        /// <summary>
        /// Предотвращает повторный запуск сервера.
        /// </summary>
        private void InnerStart()
        {
            Debug.Assert(Monitor.IsEntered(StartLock));

            if (_stopRequired == null)
            {
                if (!_started)
                {
                    _started = true;
                    if (_serviceProvider == null)
                    {
                        _serviceProvider = _serviceCollection.BuildServiceProvider();
                        _configureApp?.Invoke(_serviceProvider);
                    }

                    // Начать принимать подключения.
                    _wsServ.StartAccept();
                }
            }
        }

        private void Listener_OnConnected(object sender, DanilovSoft.WebSocket.ClientConnectedEventArgs e)
        {
            // Возможно сервер находится в режиме остановки.
            ServerSideConnection connection;
            lock (_connections.SyncObj)
            {
                if (_stopRequired == null) // volatile.
                {
                    // Создать контекст для текущего подключения.
                    connection = new ServerSideConnection(e.WebSocket, _serviceProvider, listener: this);
                    
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
                connection.InitStartThreads();

                // Сначала нужно запустить чтение, а потом вызвать событие.
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(connection));

                // Состояние гонки здесь отсутствует.
                // Событие гарантирует что обрыв пропущен не будет.
                connection.Disconnected += Context_Disconnected;
            }
        }

        private static async void CloseWebSocket(ManagedWebSocket ws, string closeDescription)
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

        private void Context_Disconnected(object sender, SocketDisconnectedEventArgs e)
        {
            var context = (ServerSideConnection)sender;
            lock (_connections.SyncObj)
            {
                _connections.Remove(context);
            }
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(context, e.DisconnectReason));
        }

        public ServerSideConnection[] GetConnectionsExcept(ServerSideConnection self)
        {
            lock (_connections.SyncObj)
            {
                int selfIndex = _connections.IndexOf(self);
                if (selfIndex != -1)
                {
                    if (_connections.Count == 1)
                    // Только мы подключены.
                    {
                        return Array.Empty<ServerSideConnection>();
                    }
                    else
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
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _wsServ.Dispose();
                _serviceProvider?.Dispose();

                ServerSideConnection[] connections;
                lock (_connections.SyncObj)
                {
                    connections = _connections.ToArray();
                    _connections.Clear();
                }

                foreach (var con in connections)
                {
                    con.Dispose();
                }
            }
        }
    }
}
