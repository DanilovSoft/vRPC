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
        private volatile ClientSideConnection _context;
        /// <summary>
        /// Завершается если подключение разорвано или не установлено.
        /// Не бросает исключения.
        /// </summary>
        public Task<CloseReason> Completion => _context?.Completion ?? Task.FromResult(CloseReason.NoConnectionError);
        public bool IsConnected => _context?.IsConnected ?? false;
        private volatile bool _stopRequired;
        private bool _disposed;
        /// <summary>
        /// Для доступа к <see cref="_disposed"/> и <see cref="_stopRequired"/>.
        /// </summary>
        private object DisposeLock => _proxyCache;

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
        /// Производит предварительное подключение к серверу. Может использоваться для повторного переподключения.
        /// Может произойти исключение после вызова Dispose или если был вызван Stop.
        /// Потокобезопасно.
        /// </summary>
        /// <exception cref="StopRequiredException"/>
        /// <exception cref="ObjectDisposedException"/>
        public Task<ConnectResult> ConnectAsync()
        {
            ThrowIfDisposed();

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

        /// <summary>
        /// 
        /// </summary>
        public T GetProxy<T>()
        {
            return _proxyCache.GetProxy<T>(ContextCallback);
        }

        /// <summary>
        /// Начинает грациозную остановку. Не блокирует поток.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public void Stop(TimeSpan timeout, string closeDescription = null)
        {
            BeginStop(timeout, closeDescription).GetAwaiter();
        }

        /// <summary>
        /// Выполняет грациозную остановку. Блокирует выполнение не дольше чем задано в <paramref name="timeout"/>.
        /// Возвращает <see langword="true"/> если разъединение завершено грациозно.
        /// Результат остановки можно получить через <see cref="Completion"/>.
        /// Потокобезопасно.
        /// </summary>
        /// <param name="timeout">Максимальное время ожидания завершения выполняющихся запросов.</param>
        /// <param name="closeDescription">Причина закрытия соединения которая будет передана удалённой стороне.</param>
        public Task<bool> StopAsync(TimeSpan timeout, string closeDescription = null)
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
            // Установлен флаг _stopRequired.
            {
                return new ValueTask<ManagedConnection>(
                   Task.FromException<ManagedConnection>(new StopRequiredException()));
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
            // Есть живое соединение.
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

        private async ValueTask<ConnectionResult> LockAquiredConnectAsync(ChannelLock.Releaser conLock)
        {
            using (conLock)
            {
                // Копия volatile.
                var context = _context;

                if (context == null)
                {
                    // Мы в блокировке поэтому можно безопасно проверить свойство.
                    if (_stopRequired)
                        throw new StopRequiredException();

                    ServiceProvider serviceProvider = ServiceProvider;
                    lock (DisposeLock)
                    {
                        if (!_disposed)
                        {
                            if (serviceProvider == null)
                            {
                                var serviceCollection = new ServiceCollection();
                                serviceProvider = ConfigureIoC(serviceCollection);
                                ServiceProvider = serviceProvider;
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
                    var ws = new MyClientWebSocket();
                    ws.Options.KeepAliveInterval = _appBuilder.KeepAliveInterval;
                    ws.Options.ReceiveTimeout = _appBuilder.ReceiveTimeout;

                    ReceiveResult receiveResult;
                    try
                    {
                        // Простое подключение веб-сокета.
                        receiveResult = await ws.ConnectExAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    // Не удалось подключиться (сервер не запущен?).
                    {
                        ws.Dispose();
                        Debug.WriteLine(ex);
                        throw;
                    }

                    if (receiveResult.IsReceivedSuccessfully)
                    {
                        lock (DisposeLock)
                        {
                            if (!_disposed)
                            {
                                context = new ClientSideConnection(this, ws, serviceProvider, _controllers);

                                // Косвенно устанавливает флаг IsConnected.
                                _context = context;
                            }
                            else
                            {
                                ws.Dispose();
                                throw new ObjectDisposedException(GetType().FullName);
                            }
                        }

                        context.StartReceivingLoop();
                        context.Disconnected += Disconnected;

                        return new ConnectionResult(ReceiveResult.AllSuccess, context);
                    }
                    else
                    // Подключение не удалось.
                    {
                        ws.Dispose();
                        return new ConnectionResult(receiveResult, null);
                    }
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

        private void ThrowIfDisposed()
        {
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
                    ServiceProvider?.Dispose();
                }
            }
        }
    }
}
