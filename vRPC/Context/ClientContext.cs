using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using MyClientWebSocket = DanilovSoft.WebSocket.ClientWebSocket;
using MyWebSocket = DanilovSoft.WebSocket.WebSocket;

namespace vRPC
{
    /// <summary>
    /// Подключенный к серверу клиент.
    /// </summary>
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public sealed class ClientContext : Context
    {
        private const string PassPhrase = "Pas5pr@se";        // Может быть любой строкой.
        private const string InitVector = "@1B2c3D4e5F6g7H8"; // Должно быть 16 байт.

        /// <summary>
        /// Сервер который принял текущее соединение.
        /// </summary>
        public Listener Listener { get; }
        //private volatile UserConnections _userConnections;
        ///// <summary>
        ///// Смежные соединения текущего пользователя. Является <see langword="volatile"/>.
        ///// </summary>
        //public UserConnections UserConnections { get => _userConnections; private set => _userConnections = value; }

        // ctor.
        internal ClientContext(MyWebSocket clientConnection, ServiceProvider serviceProvider, Listener listener) 
            : base(clientConnection, serviceProvider, listener.Controllers)
        {
            Listener = listener;

            //_jwt = new RijndaelEnhanced(PassPhrase, InitVector);
        }

        // Вызывается после конструктора.
        internal void StartReceive()
        {
            // Начать обработку запросов текущего пользователя.
            StartReceivingLoop();
        }

        ///// <summary>
        ///// Производит авторизацию текущего подключения.
        ///// </summary>
        ///// <param name="userId"></param>
        ///// <exception cref="BadRequestException"/>
        //public BearerToken Authorize(int userId)
        //{
        //    // Функцию могут вызвать из нескольких потоков.
        //    lock (_syncObj)
        //    {
        //        InnerAuthorize(userId);

        //        var tokenValidity = TimeSpan.FromDays(2);
        //        var serverBearer = new ServerBearerToken
        //        {
        //            UserId = userId,
        //            Validity = DateTime.Now + tokenValidity,
        //        };

        //        byte[] serialized;
        //        using (var mem = new MemoryPoolStream(capacity: 18))
        //        {
        //            ProtoBuf.Serializer.Serialize(mem, serverBearer);
        //            serialized = mem.ToArray();
        //        }

        //        // Закриптовать в бинарник идентификатор пользователя.
        //        byte[] encryptedToken = _jwt.EncryptToBytes(serialized);

        //        var token = new BearerToken
        //        {
        //            Key = encryptedToken,
        //            ExpiresAt = tokenValidity
        //        };

        //        return token;
        //    }
        //}

        ///// <summary>
        ///// Производит авторизацию текущего подключения по токену.
        ///// </summary>
        ///// <param name="userId"></param>
        ///// <exception cref="BadRequestException"/>
        //public bool AuthorizeToken(byte[] encriptedToken)
        //{
        //    // Расшифрованный токен полученный от пользователя.
        //    byte[] decripted;

        //    try
        //    {
        //        // Расшифровать токен.
        //        decripted = _jwt.DecryptToBytes(encriptedToken);
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine(ex);

        //        // Токен не валиден.
        //        return false;
        //    }

        //    ServerBearerToken bearerToken;
        //    try
        //    {
        //        using (var mem = new MemoryStream(decripted))
        //            bearerToken = ProtoBuf.Serializer.Deserialize<ServerBearerToken>(mem);
        //    }
        //    catch (Exception ex)
        //    {
        //        Debug.WriteLine(ex);

        //        // Токен не валиден.
        //        return false;
        //    }

        //    if (DateTime.Now < bearerToken.Validity)
        //    // Токен валиден.
        //    {
        //        // Безусловная авторизация.
        //        InnerAuthorize(bearerToken.UserId);
        //        return true;
        //    }

        //    // Токен не валиден.
        //    return false;
        //}

        ///// <summary>
        ///// Потокобезопасно производит авторизацию текущего соединения.
        ///// </summary>
        ///// <param name="userId">Идентификатор пользователя который будет пазначен текущему контексту.</param>
        //private void InnerAuthorize(int userId)
        //{
        //    // Функцию могут вызвать из нескольких потоков.
        //    lock (_syncObj)
        //    {
        //        if (!IsAuthorized)
        //        {
        //            // Авторизуем контекст пользователя.
        //            UserId = userId;
        //            IsAuthorized = true;

        //            // Добавляем соединение в словарь.
        //            UserConnections = AddConnection(userId);
        //        }
        //        else
        //            throw new BadRequestException($"You are already authorized as 'UserId: {UserId}'", StatusCode.BadRequest);
        //    }
        //}

        /// <summary>
        /// Потокобезопасно добавляет текущее соединение в словарь или создаёт новый словарь.
        /// </summary>
        private UserConnections AddConnection(int userId)
        {
            do
            {
                // Берем существующую структуру или создаем новую.
                UserConnections userConnections = Listener.Connections.GetOrAdd(userId, uid => new UserConnections(uid));

                // Может случиться так что мы взяли существующую коллекцию но её удаляют из словаря в текущий момент.
                lock (userConnections.SyncRoot) // Захватить эксклюзивный доступ.
                {
                    // Если коллекцию еще не удалили из словаря то можем безопасно добавить в неё соединение.
                    if (!userConnections.IsDestroyed)
                    {
                        userConnections.Add(this);
                        return userConnections;
                    }
                }
            } while (true);
        }

        //protected override void BeforeInvokePrepareController(Controller controller)
        //{
        //    var serverController = (ServerController)controller;
        //    serverController.Context = this;
        //    //serverController.Listener = Listener;
        //}

        /// <summary>
        /// Проверяет доступность запрашиваемого метода пользователем.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        protected override void InvokeMethodPermissionCheck(MethodInfo method, Type controllerType)
        {
            //// Проверить доступен ли метод пользователю.
            //if (IsAuthorized)
            //    return;

            // Разрешить если метод помечен как разрешенный для не авторизованных пользователей.
            if (Attribute.IsDefined(method, typeof(AllowAnonymousAttribute)))
                return;

            // Разрешить если контроллер помечен как разрешенный для не акторизованных пользователей.
            if (Attribute.IsDefined(controllerType, typeof(AllowAnonymousAttribute)))
                return;

            throw new BadRequestException("This action requires user authentication.", StatusCode.Unauthorized);
        }
    }
}
