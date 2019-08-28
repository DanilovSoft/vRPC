using DanilovSoft.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace vRPC
{
    public sealed class Listener : IDisposable
    {
        /// <summary>
        /// Словарь используемый только для чтения, поэтому потокобезопасен.
        /// Хранит все доступные контроллеры. Не учитывает регистр.
        /// </summary>
        internal readonly ControllerActionsDictionary Controllers;
        private readonly WebSocketServer _wsServ;
        /// <summary>
        /// Доступ через блокировку SyncObj.
        /// </summary>
        private readonly ClientConnections _connections = new ClientConnections();
        private readonly ServiceCollection _ioc;
        private readonly object _startObj = new object();
        private readonly TaskCompletionSource<bool> _completionTcs = new TaskCompletionSource<bool>();
        /// <summary>
        /// <see cref="Task"/> который завершается когда все 
        /// соединения перешли в закрытое состояние и сервис полностью остановлен.
        /// Не бросает исключения.
        /// Возвращает <see langword="true"/> если остановка прошла грациозно.
        /// </summary>
        public Task<bool> Completion => _completionTcs.Task;
        private bool _started;
        /// <summary>
        /// Коллекция авторизованных пользователей.
        /// Ключ словаря — UserId авторизованного пользователя.
        /// </summary>
        public ConcurrentDictionary<int, UserConnections> Connections { get; } = new ConcurrentDictionary<int, UserConnections>();
        private bool _disposed;
        private ServiceProvider _serviceProvider;
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;
        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;
        private Action<ServiceCollection> _iocConfigure;
        private Action<ServiceProvider> _configureApp;
        /// <summary>
        /// Не позволяет подключаться новым клиентам.
        /// </summary>
        private volatile bool _stopRequired;
        public TimeSpan ClientKeepAliveInterval { get => _wsServ.ClientKeepAliveInterval; set => _wsServ.ClientKeepAliveInterval = value; }
        public TimeSpan ClientReceiveTimeout { get => _wsServ.ClientReceiveTimeout; set => _wsServ.ClientReceiveTimeout = value; }

        static Listener()
        {
            Warmup.DoWarmup();
        }

        // ctor.
        public Listener(IPAddress ipAddress, int port)
        {
            _ioc = new ServiceCollection();
            _wsServ = new WebSocketServer();
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
                _ioc.AddScoped(controllerType);
            }
        }

        /// <summary>
        /// Позволяет настроить IoC контейнер.
        /// Выполняется единожды при инициализации подключения.
        /// </summary>
        /// <param name="configure"></param>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            _iocConfigure = configure;
        }

        public void Configure(Action<ServiceProvider> configureApp)
        {
            _configureApp = configureApp;
        }

        /// <summary>
        /// Начинает остановку сервиса.
        /// Не бросает исключения.
        /// Результат можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания грациозного закрытия соединений.</param>
        public void Stop(TimeSpan timeout)
        {
            BeginStop(timeout);
        }

        /// <summary>
        /// Останавливает сервис и ожидает до полной остановки.
        /// Не бросает исключения.
        /// Эквивалентно <see cref="Stop(TimeSpan)"/> + <see langword="await"/> <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания грациозного закрытия соединений.</param>
        public Task<bool> StopAsync(TimeSpan timeout)
        {
            BeginStop(timeout);
            return Completion;
        }

        private async void BeginStop(TimeSpan timeout)
        {
            lock (_startObj)
            {
                if (!_stopRequired)
                {
                    _stopRequired = true;

                    // Прекратить приём новых соединений.
                    _wsServ.Dispose();
                }
            }

            // Необходимо закрыть все соединения.

            ServerSideConnection[] activeConnections;
            lock (_connections.SyncObj)
            {
                // Теперь мы обязаны сделать Dispose этим подключениям.
                activeConnections = _connections.ToArray();
                _connections.Clear();
            }

            // Грациозно останавливаем соединения.
            foreach (var con in activeConnections)
            {
                // Прекращаем принимать запросы.
                con.RequireStop();
            }

            var allConnTask = Task.WhenAll(activeConnections.Select(x => x.Completion));
            var timeoutTask = Task.Delay(timeout);

            bool gracefully;
            if (await Task.WhenAny(allConnTask, timeoutTask).ConfigureAwait(false) != timeoutTask)
            {
                gracefully = true;
            }
            else
            // Истекло время ожидания грациозного завершения.
            {
                gracefully = false;
            }

            foreach (var con in activeConnections)
            {
                con.CloseAndDispose();
            }

            // Установить Completion.
            _completionTcs.TrySetResult(gracefully);
        }

        /// <summary>
        /// Начинает приём подключений и обработку запросов до полной остановки методом Stop.
        /// Эквивалентно вызову Start + <see langword="await"/> Completion.
        /// </summary>
        public Task<bool> RunAsync()
        {
            // Бросит исключение при повторном вызове.
            InitializeStart();
            _wsServ.StartAccept();
            return Completion;
        }

        /// <summary>
        /// Потокобезопасно начинает приём новых подключений.
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        public void Start()
        {
            // Бросит исключение при повторном вызове.
            InitializeStart();
            _wsServ.StartAccept();
        }

        /// <summary>
        /// Предотвращает повторный запуск сервера.
        /// </summary>
        private void InitializeStart()
        {
            lock (_startObj)
            {
                if (!_stopRequired)
                {
                    if (!_started)
                    {
                        _started = true;
                        _iocConfigure?.Invoke(_ioc);
                        _serviceProvider = _ioc.BuildServiceProvider();
                        _configureApp?.Invoke(_serviceProvider);
                    }
                    else
                        throw new InvalidOperationException("Already started.");
                }
                else
                    throw new InvalidOperationException("Already stopped.");
            }
        }

        private void Listener_OnConnected(object sender, DanilovSoft.WebSocket.ClientConnectedEventArgs e)
        {
            // Создать контекст для текущего подключения.
            var context = new ServerSideConnection(e.Connection, _serviceProvider, listener: this);

            // Возможно сервер находится в режиме остановки.
            bool connectionAllowed;
            lock(_connections.SyncObj)
            {
                if (!_stopRequired) // volatile.
                {
                    _connections.Add(context);
                    connectionAllowed = true;
                }
                else
                {
                    context.Dispose();
                    connectionAllowed = false;
                }
            }

            if (connectionAllowed)
            {
                // Сервер разрешил установку этого соединения, можно начать чтение.
                context.StartReceive();

                // Сначала нужно запустить чтение, а потом вызвать событие.
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(context));

                // Состояние гонки здесь отсутствует.
                // Событие гарантирует что обрыв пропущен не будет.
                context.Disconnected += Context_Disconnected;
            }
        }

        private void Context_Disconnected(object sender, SocketDisconnectedEventArgs e)
        {
            var context = (ServerSideConnection)sender;
            lock (_connections.SyncObj)
            {
                _connections.Remove(context);
            }
            ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(context, e.ReasonException));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _wsServ.Dispose();
                _serviceProvider?.Dispose();

                ServerSideConnection[] copy;
                lock (_connections.SyncObj)
                {
                    copy = _connections.ToArray();
                    _connections.Clear();
                }

                foreach (var con in copy)
                {
                    con.Dispose();
                }
            }
        }
    }
}
