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
        private Action<ServiceCollection> _iocConfigure;
        private Action<ApplicationBuilder> _configureApp;
        private volatile ClientSideConnection _context;
        /// <summary>
        /// Завершается если подключение отсутствует или разорвано.
        /// Не бросает исключения.
        /// </summary>
        public Task Completion => _context?.Completion ?? Task.CompletedTask;
        public bool IsConnected => _context?.IsConnected ?? false;
        public Exception DisconnectReason => _context?.DisconnectReason;

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
            _iocConfigure = configure;
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
        public async Task<SocketError> ConnectAsync()
        {
            ConnectionResult connectionResult = await ConnectIfNeededAsync().ConfigureAwait(false);
            return connectionResult.SocketError;
        }

        public T GetProxy<T>()
        {
            return _proxyCache.GetProxy<T>(ContextCallback);
        }

        private async ValueTask<ManagedConnection> ContextCallback()
        {
            ConnectionResult connectionResult = await ConnectIfNeededAsync().ConfigureAwait(false);

            if (connectionResult.SocketError == SocketError.Success)
                return connectionResult.Context;

            throw connectionResult.SocketError.ToException();
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
        private async ValueTask<ConnectionResult> ConnectIfNeededAsync()
        {
            // Копия volatile.
            var context = _context;
            if (context == null)
            {
                using (await _connectLock.LockAsync().ConfigureAwait(false))
                {
                    // Копия volatile.
                    context = _context;
                    if (context == null)
                    {
                        var serviceCollection = new ServiceCollection();
                        _iocConfigure?.Invoke(serviceCollection);
                        ServiceProvider serviceProvider = ConfigureIoC(serviceCollection);
                        _appBuilder = new ApplicationBuilder(serviceProvider);
                        _configureApp?.Invoke(_appBuilder);

                        // Новый сокет.
                        var ws = new MyClientWebSocket();
                        ws.Options.KeepAliveInterval = _appBuilder.KeepAliveInterval;
                        ws.Options.ReceiveTimeout = _appBuilder.ReceiveTimeout;

                        SocketError errorCode;
                        try
                        {
                            // Простое подключение веб-сокета.
                            errorCode = await ws.ConnectExAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        // Не удалось подключиться (сервер не запущен?).
                        {
                            serviceProvider.Dispose();
                            ws.Dispose();
                            throw;
                        }

                        if (errorCode == SocketError.Success)
                        {
                            context = new ClientSideConnection(this, ws, serviceProvider, _controllers);

                            // Косвенно устанавливает флаг IsConnected.
                            _context = context;

                            context.StartReceivingLoop();
                            context.Disconnected += Disconnected;
                        }
                        else
                        {
                            ws.Dispose();
                            return new ConnectionResult(errorCode, null);
                        }
                        return new ConnectionResult(SocketError.Success, context);
                    }
                    else
                        return new ConnectionResult(SocketError.Success, context);
                }
            }
            else
                return new ConnectionResult(SocketError.Success, context);
        }

        /// <summary>
        /// Вызывается единожды клиентским контектом.
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

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}
