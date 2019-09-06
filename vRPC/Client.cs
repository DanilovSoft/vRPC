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
using MyClientWebSocket = DanilovSoft.WebSocket.ClientWebSocket;

namespace vRPC
{
    /// <summary>
    /// Контекст клиентского соединения.
    /// </summary>
    [DebuggerDisplay(@"\{Connected = {IsConnected}\}")]
    public sealed class Client : IDisposable
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
        //private Action<ServiceCollection> _iocConfigure;
        private ServiceProvider _serviceProvider;
        public ServiceProvider ServiceProvider => _serviceProvider;
        private Action<ApplicationBuilder> _configureApp;
        private volatile ClientSideConnection _context;
        /// <summary>
        /// Завершается если подключение разорвано или не установлено.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion => _context?.Completion ?? Task.FromResult<CloseReason>(CloseReason.NotConnectedError);
        public bool IsConnected => _context?.IsConnected ?? false;
        private volatile bool _stopRequired;
        private bool _disposed;
        private object DisposeLock => _proxyCache;

        static Client()
        {
            Warmup.DoWarmup();
        }

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        public Client(Uri uri) : this(Assembly.GetCallingAssembly(), uri)
        {

        }

        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        public Client(string host, int port) : this(Assembly.GetCallingAssembly(), new Uri($"ws://{host}:{port}"))
        {
            
        }

        /// <summary>
        /// Конструктор клиента.
        /// </summary>
        /// <param name="controllersAssembly">Сборка в которой осуществляется поиск контроллеров.</param>
        /// <param name="uri">Адрес сервера.</param>
        private Client(Assembly controllersAssembly, Uri uri)
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
        /// <param name="configure"></param>
        public void ConfigureService(Action<ServiceCollection> configure)
        {
            if (_serviceProvider != null)
                throw new InvalidOperationException("Service already configured.");

            var serviceCollection = new ServiceCollection();
            configure(serviceCollection);
            _serviceProvider = ConfigureIoC(serviceCollection);
        }

        public void Configure(Action<ApplicationBuilder> configureApp)
        {
            _configureApp = configureApp;
        }

        /// <summary>
        /// Производит предварительное подключение сокета к серверу. Может использоваться для повторного переподключения.
        /// Может произойти исключение если одновременно вызвать Dispose.
        /// Потокобезопасно.
        /// </summary>
        public Task<ConnectResult> ConnectAsync()
        {
            var t = ConnectIfNeededAsync();
            if(t.IsCompleted)
            {
                var conRes = t.Result;
                return Task.FromResult(new ConnectResult(conRes.ReceiveResult.IsReceivedSuccessfully, conRes.ReceiveResult.SocketError));
            }
            else
            {
                return WaitForConnectAsync(t);
            }

            static async Task<ConnectResult> WaitForConnectAsync(ValueTask<ConnectionResult> t)
            {
                ConnectionResult conRes = await t.ConfigureAwait(false);
                return new ConnectResult(conRes.ReceiveResult.IsReceivedSuccessfully, conRes.ReceiveResult.SocketError);
            }
        }

        public T GetProxy<T>()
        {
            return _proxyCache.GetProxy<T>(ContextCallback);
        }

        /// <summary>
        /// Начинает грациозную остановку. Не блокирует поток.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        public void Stop(TimeSpan timeout)
        {
            Stop(timeout, null);
        }

        /// <summary>
        /// Начинает грациозную остановку. Не блокирует поток.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        public void Stop(TimeSpan timeout, string closeDescription)
        {
            BeginStop(timeout, closeDescription).GetAwaiter();
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="timeout"/>.
        /// Возвращает <see langword="true"/> если разъединение завершено грациозно.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        public Task<bool> StopAsync(TimeSpan timeout)
        {
            return StopAsync(timeout, null);
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="timeout"/>.
        /// Возвращает <see langword="true"/> если разъединение завершено грациозно.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.
        /// Может быть <see langword="null"/>.</param>
        public Task<bool> StopAsync(TimeSpan timeout, string closeDescription)
        {
            return BeginStop(timeout, closeDescription);
        }

        private async Task<bool> BeginStop(TimeSpan timeout, string closeDescription)
        {
            // Больше клиент не должен переподключаться.
            _stopRequired = true;

            var context = _context;
            if (context != null)
            {
                context.RequireStop(closeDescription);

                var timeoutTask = Task.Delay(timeout);
                var t = await Task.WhenAny(context.Completion, Task.Delay(timeout)).ConfigureAwait(false);

                // Не бросает исключения.
                context.CloseAndDispose(timeout);

                bool gracefully = t != timeoutTask;
                return gracefully;
            }
            return true;
        }

        private ValueTask<ManagedConnection> ContextCallback()
        {
            if (!_stopRequired)
            {
                // Копия volatile.
                var context = _context;
                if (context != null)
                {
                    return new ValueTask<ManagedConnection>(context);
                }
                else
                {
                    var t = ConnectIfNeededAsync();
                    if (t.IsCompleted)
                    {
                        ConnectionResult connectionResult = t.Result;
                        if (connectionResult.ReceiveResult.IsReceivedSuccessfully)
                            return new ValueTask<ManagedConnection>(connectionResult.Context);

                        return new ValueTask<ManagedConnection>(Task.FromException<ManagedConnection>(connectionResult.ReceiveResult.ToException()));
                    }
                    else
                    {
                        return WaitForConnectIfNeededAsync(t);
                    }
                }
            }
            else
            {
                return new ValueTask<ManagedConnection>(
                   Task.FromException<ManagedConnection>(new InvalidOperationException("Был вызван Stop — использовать этот экземпляр больше нельзя.")));
            }
        }

        private static async ValueTask<ManagedConnection> WaitForConnectIfNeededAsync(ValueTask<ConnectionResult> t)
        {
            ConnectionResult connectionResult = await t.ConfigureAwait(false);

            if (connectionResult.ReceiveResult.IsReceivedSuccessfully)
                return connectionResult.Context;

            throw connectionResult.ReceiveResult.ToException();
        }

        /// <summary>
        /// Событие — обрыв сокета. Потокобезопасно. Срабатывает только один раз.
        /// </summary>
        private void Disconnected(object sender, SocketDisconnectedEventArgs e)
        {
            _context = null;
        }

        /// <summary>
        /// Выполнить подключение сокета если еще не подключен.
        /// </summary>
        private ValueTask<ConnectionResult> ConnectIfNeededAsync()
        {
            // Копия volatile.
            var context = _context;
            if (context != null)
            {
                return new ValueTask<ConnectionResult>(new ConnectionResult(ReceiveResult.AllSuccess, context));
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

        private async ValueTask<ConnectionResult> LockAquiredConnectAsync(ChannelLock.Releaser releaser)
        {
            using (releaser)
            {
                // Копия volatile.
                var context = _context;
                if (context == null)
                {
                    ServiceProvider serviceProvider = _serviceProvider;
                    if (serviceProvider == null)
                    {
                        var serviceCollection = new ServiceCollection();
                        serviceProvider = ConfigureIoC(serviceCollection);
                        lock(DisposeLock)
                        {
                            if (!_disposed)
                            {
                                _serviceProvider = serviceProvider;
                            }
                            else
                            {
                                serviceProvider.Dispose();
                                throw new ObjectDisposedException(GetType().FullName);
                            }
                        }
                    }

                    _appBuilder = new ApplicationBuilder(serviceProvider);
                    _configureApp?.Invoke(_appBuilder);

                    // Новый сокет.
                    var ws = new MyClientWebSocket();
                    ws.Options.KeepAliveInterval = _appBuilder.KeepAliveInterval;
                    ws.Options.ReceiveTimeout = _appBuilder.ReceiveTimeout;

                    ReceiveResult receiveResult;
                    try
                    {
                        // Простое подключение веб-сокета.
                        receiveResult = await ws.ConnectExAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    // Не удалось подключиться (сервер не запущен?).
                    {
                        ws.Dispose();
                        throw;
                    }

                    if (receiveResult.IsReceivedSuccessfully)
                    {
                        context = new ClientSideConnection(this, ws, serviceProvider, _controllers);

                        lock (DisposeLock)
                        {
                            if (!_disposed)
                            {
                                // Косвенно устанавливает флаг IsConnected.
                                _context = context;
                            }
                            else
                            {
                                context.Dispose();
                                throw new ObjectDisposedException(GetType().FullName);
                            }
                        }

                        context.StartReceivingLoop();
                        context.Disconnected += Disconnected;
                    }
                    else
                    {
                        ws.Dispose();
                        return new ConnectionResult(receiveResult, null);
                    }
                    return new ConnectionResult(ReceiveResult.AllSuccess, context);
                }
                else
                    return new ConnectionResult(ReceiveResult.AllSuccess, context);
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

        private void ThrowIfDispose()
        {
            Debug.Assert(Monitor.IsEntered(DisposeLock));

            if (!_disposed)
                return;

            throw new ObjectDisposedException(GetType().FullName);
        }

        public void Dispose()
        {
            lock (DisposeLock)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _context?.Dispose();
                    _serviceProvider?.Dispose();
                }
            }
        }
    }
}
