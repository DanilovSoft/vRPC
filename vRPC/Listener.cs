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
        internal readonly Dictionary<string, Type> Controllers;
        private readonly WebSocketServer _wsServ;
        /// <summary>
        /// Доступ через блокировку SyncObj.
        /// </summary>
        private readonly ClientConnections _connections = new ClientConnections();
        private readonly ServiceCollection _ioc;
        private readonly object _startObj = new object();
        private readonly TaskCompletionSource<int> _runTcs = new TaskCompletionSource<int>();
        public Task Completion => _runTcs.Task;
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
        private volatile bool _stopRequired;

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
            Controllers = GlobalVars.FindAllControllers(controllersAssembly);

            // Добавить контроллеры в IoC.
            foreach (Type controllerType in Controllers.Values)
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
        /// Не бросает исключения.
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        public async Task<bool> StopAsync(TimeSpan timeout)
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

            ClientContext[] copy;
            lock (_connections.SyncObj)
            {
                // Теперь мы обязаны сделать Dispose этим подключениям.
                copy = _connections.ToArray();
                _connections.Clear();
            }

            // Грациозно останавливаем соединения.
            foreach (var con in copy)
            {
                // Прекращаем принимать запросы.
                con.StopRequired();
            }

            var allConnTask = Task.WhenAll(copy.Select(x => x.Completion));
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

            foreach (var con in copy)
            {
                con.StopAndDispose();
            }

            _runTcs.TrySetResult(0);
            return gracefully;
        }

        public Task RunAsync()
        {
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
            var context = new ClientContext(e.Connection, _serviceProvider, listener: this);

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
            var context = (ClientContext)sender;
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

                ClientContext[] copy;
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
