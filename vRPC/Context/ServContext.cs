using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyClientWebSocket = DanilovSoft.WebSocket.ClientWebSocket;

namespace vRPC
{
    /// <summary>
    /// Контекст клиентского соединения.
    /// </summary>
    [DebuggerDisplay("{DebugDisplay,nq}")]
    public sealed class ServerContext : Context
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebugDisplay => "{" + $"Connected = {Socket != null}, IsAuthorized = {IsAuthorized}" + "}";

        /// <summary>
        /// Используется для синхронизации установки соединения.
        /// </summary>
        private readonly ChannelLock _channelLock;
        /// <summary>
        /// Адрес для подключеия к серверу.
        /// </summary>
        private readonly Uri _uri;
        private readonly object _idleObj = new object();
        private readonly ApplicationBuilder _appBuilder = new ApplicationBuilder();
        /// <summary>
        /// Токен авторизации передаваемый серверу при начальном подключении.
        /// </summary>
        public byte[] BearerToken { get; set; }
        /// <summary>
        /// <see langword="true"/> если соединение авторизовано на сервере.
        /// </summary>
        public bool IsAuthorized { get; private set; }
        private Action<ServiceCollection> _iocConfigure;
        private Action<ApplicationBuilder> _configureApp;
        /// <summary>
        /// Доступ только через блокировку _idleObj.
        /// </summary>
        private TaskCompletionSource<int> _idleTask;

        // ctor.
        /// <summary>
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        public ServerContext(Uri uri) : this(Assembly.GetCallingAssembly(), uri)
        {

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
        /// Создаёт контекст клиентского соединения.
        /// </summary>
        public ServerContext(string host, int port) : this(Assembly.GetCallingAssembly(), new Uri($"ws://{host}:{port}"))
        {
            
        }

        /// <summary>
        /// Конструктор клиента.
        /// </summary>
        /// <param name="controllersAssembly">Сборка в которой осуществляется поиск контроллеров.</param>
        /// <param name="uri">Адрес сервера.</param>
        internal ServerContext(Assembly controllersAssembly, Uri uri) : base(controllersAssembly)
        {
            _uri = uri;
            _channelLock = new ChannelLock();
        }

        /// <summary>
        /// Производит предварительное подключение сокета к серверу. Может использоваться для повторного переподключения.
        /// Потокобезопасно.
        /// </summary>
        public async Task<Task> ConnectAsync()
        {
            await ConnectIfNeededAsync().ConfigureAwait(false);
            lock(_idleObj)
            {
                return _idleTask.Task;
            }
        }

        ///// <summary>
        ///// Блокирует продолжение пока установлено соединение с сервером.
        ///// Завершается если соединение не установлено.
        ///// Бросает исключение при обрыве.
        ///// Потокобезопасно.
        ///// </summary>
        //public async Task IdleAsync()
        //{
        //    await _idleTask.Task.ConfigureAwait(false);
        //}

        /// <summary>
        /// Событие — обрыв сокета. Потокобезопасно срабатывает только один раз.
        /// </summary>
        private protected override void OnAtomicDisconnect(SocketWrapper socketQueue, Exception exception)
        {
            lock (_idleObj)
            {
                _socket = null;
                _idleTask.TrySetException(exception);
            }
        }

        private protected override Task<SocketWrapper> GetOrCreateConnectionAsync() => ConnectIfNeededAsync();

        /// <summary>
        /// Выполнить подключение сокета если еще не подключен.
        /// </summary>
        private async Task<SocketWrapper> ConnectIfNeededAsync()
        {
            // Копия volatile ссылки.
            SocketWrapper socketQueue = Socket;

            // Fast-path.
            if (socketQueue != null)
                return socketQueue;

            using (await _channelLock.LockAsync().ConfigureAwait(false))
            {
                // Копия volatile ссылки.
                socketQueue = _socket;

                // Необходима повторная проверка.
                if (socketQueue == null)
                {
                    if (ServiceProvider == null)
                    {
                        var ioc = new ServiceCollection();
                        _iocConfigure?.Invoke(ioc);
                        _appBuilder.ServiceProvider = ServiceProvider;
                        _configureApp?.Invoke(_appBuilder);
                        ConfigureIoC(ioc);
                    }

                    // Новый сокет.
                    var ws = new MyClientWebSocket();
                    ws.Options.KeepAliveInterval = _appBuilder.KeepAliveInterval;
                    ws.Options.ReceiveTimeout = _appBuilder.ReceiveTimeout;

                    try
                    {
                        // Простое подключение веб-сокета.
                        await ws.ConnectAsync(_uri, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    // Не удалось подключиться (сервер не запущен?).
                    {
                        ws.Dispose();
                        throw;
                    }

                    // Управляемая обвертка для сокета.
                    socketQueue = new SocketWrapper(ws);

                    lock (_idleObj)
                    {
                        _idleTask = new TaskCompletionSource<int>();
                    }

                    // Начать бесконечное чтение из сокета. — может преждевременно вызвать AtomicDisconnect.
                    StartReceivingLoop(socketQueue);

                    // Копируем ссылку на публичный токен.
                    byte[] bearerTokenCopy = BearerToken;

                    // Если токен установлен то отправить его на сервер что-бы авторизовать текущее подключение.
                    if (bearerTokenCopy != null)
                    {
                        try
                        {
                            IsAuthorized = await AuthorizeAsync(socketQueue, bearerTokenCopy).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        // Обрыв соединения.
                        {
                            // Оповестить об обрыве.
                            AtomicDisconnect(socketQueue, ex);
                            throw;
                        }
                    }

                    Debug.WriteIf(IsAuthorized, "Соединение успешно авторизовано");

                    Task idleTask = null;
                    lock (_idleObj)
                    {
                        if (_idleTask.Task.Status == TaskStatus.WaitingForActivation)
                        {
                            // Открыть публичный доступ к этому сокету.
                            _socket = socketQueue;
                        }
                        else
                        // Мог произойти AtomicDisconnect.
                        {
                            // В этом случае в Task будет записана причина.
                            idleTask = _idleTask.Task;
                        }
                    }

                    if (idleTask != null)
                        await idleTask;
                }
                return socketQueue;
            }
        }

        /// <summary>
        /// Отправляет специфический запрос содержащий токен авторизации. Ожидает ответ.
        /// </summary>
        private async Task<bool> AuthorizeAsync(SocketWrapper socketQueue, byte[] bearerToken)
        {
            // Запрос на авторизацию по токену.
            var requestToSend = Message.CreateRequest("Auth/AuthorizeToken", new Arg[] { new Arg("token", bearerToken) });
            
            // Отправить запрос и получить ответ.
            object result = await ExecuteRequestAsync(requestToSend, returnType: typeof(bool), socketQueue).ConfigureAwait(false);

            return (bool)result;
        }

        protected override void BeforeInvokePrepareController(Controller controller)
        {
            var clientController = (ClientController)controller;
            clientController.Context = this;
        }

        // Серверу всегда доступны методы клиента.
        protected override void InvokeMethodPermissionCheck(MethodInfo method, Type controllerType) { }

        public override void Dispose()
        {
            ServiceProvider?.Dispose();
            base.Dispose();
        }
    }
}
