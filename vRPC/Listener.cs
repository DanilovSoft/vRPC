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
using MyWebSocket = DanilovSoft.WebSocket.WebSocket;

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
        private readonly List<ClientContext> _connections = new List<ClientContext>();
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
        //private int _status;
        private ServiceProvider _serviceProvider;
        public event EventHandler<ClientConnectedEventArgs> ClientConnected;
        private Action<ServiceCollection> _iocConfigure;
        private Action<ServiceProvider> _configureApp;
        private volatile bool _stopRequired;

        // ctor.
        public Listener(IPAddress ipAddress, int port)
        {
            _ioc = new ServiceCollection();
            _wsServ = new WebSocketServer();
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
            lock (_connections)
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

            bool connected;
            lock(_connections)
            {
                if (!_stopRequired) // volatile.
                {
                    _connections.Add(context);
                    connected = true;
                }
                else
                {
                    context.Dispose();
                    connected = false;
                }
            }

            if (connected)
            {
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs(context));
                context.StartReceive();
            }
        }

        internal void OnDisconnected(ClientContext context, Exception exception)
        {
            lock (_connections)
            {
                _connections.Remove(context);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _wsServ.Dispose();
                _serviceProvider?.Dispose();

                ClientContext[] copy;
                lock (_connections)
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
