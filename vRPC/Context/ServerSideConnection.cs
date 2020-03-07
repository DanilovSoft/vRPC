using DanilovSoft.vRPC.Decorator;
using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DanilovSoft.vRPC
{
    /// <summary>
    /// Подключенный к серверу клиент.
    /// </summary>
    [DebuggerDisplay(@"\{IsConnected = {IsConnected}\}")]
    public sealed class ServerSideConnection : ManagedConnection, IGetProxy
    {
        private const string PassPhrase = "Pas5pr@se";        // Может быть любой строкой.
        private const string InitVector = "@1B2c3D4e5F6g7H8"; // Должно быть 16 байт.
        private const string Salt = "M6PgwzAnHy02Jv8z5FPIoOn5NeJP7bx7";

        internal static readonly ServerConcurrentDictionary<MethodInfo, string> ProxyMethodName = new ServerConcurrentDictionary<MethodInfo, string>();
        private static readonly ServerConcurrentDictionary<MethodInfo, ReusableRequestToSend> _interfaceMethodsInfo = new ServerConcurrentDictionary<MethodInfo, ReusableRequestToSend>();
        private readonly ProxyCache _proxyCache = new ProxyCache();
        private readonly RijndaelEnhanced _jwt;
        private object _userLock => _jwt;
        internal ClaimsPrincipal User { get; private set; }
        private protected override IConcurrentDictionary<MethodInfo, ReusableRequestToSend> InterfaceMethods => _interfaceMethodsInfo;

        /// <summary>
        /// Сервер который принял текущее соединение.
        /// </summary>
        public RpcListener Listener { get; }

        // ctor.
        // Только Listener может создать этот класс.
        internal ServerSideConnection(ManagedWebSocket clientConnection, ServiceProvider serviceProvider, RpcListener listener) 
            : base(clientConnection, isServer: true, serviceProvider, listener.InvokeActions)
        {
            Listener = listener;

            _jwt = new RijndaelEnhanced(PassPhrase, InitVector, 8, 16, 256, Salt, 1000);

            // Изначальный не авторизованный пользователь.
            User = CreateUnauthorizedUser();
        }

        private ClaimsPrincipal CreateUnauthorizedUser()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// Полученный прокси можно привести к типу <see cref="ServerInterfaceProxy"/> 
        /// что-бы получить дополнительные сведения.
        /// </summary>
        public T GetProxy<T>() where T : class
        {
            return _proxyCache.GetProxy<T>(this);
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса.
        /// </summary>
        /// <param name="decorator">Декоратор интерфейса который содержит дополнительные сведения.</param>
        public T GetProxy<T>(out ServerInterfaceProxy decorator) where T : class
        {
            var p = _proxyCache.GetProxy<T>(this);
            decorator = p as ServerInterfaceProxy;
            return p;
        }

        internal BearerToken CreateAccessToken(ClaimsPrincipal claimsPrincipal)
        {
            byte[] encryptedToken;
            using (var stream = GlobalVars.RecyclableMemory.GetStream("claims-principal", 32))
            {
                using (var bwriter = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    claimsPrincipal.WriteTo(bwriter);
                }
                byte[] serializedClaims = stream.ToArray();

                var tokenValidity = TimeSpan.FromDays(2);
                DateTime validity = DateTime.Now + tokenValidity;
                var serverBearer = new ServerAccessToken(serializedClaims, validity);

                using (var mem = GlobalVars.RecyclableMemory.GetStream())
                {
                    ProtoBuf.Serializer.Serialize(mem, serverBearer);
                    byte[] serializedTmpBuf = mem.GetBuffer();

                    // Закриптовать.
                    encryptedToken = _jwt.EncryptToBytes(serializedTmpBuf.AsSpan(0, (int)mem.Length));
                }

                var token = new BearerToken(encryptedToken, validity);

                //lock (_userLock)
                //{
                //    User = claimsPrincipal;
                //}

                return token;
            }
        }

        /// <summary>
        /// Производит аутентификацию текущего подключения.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        internal void SignIn(AccessToken accessToken)
        {
            // Расшифрованный токен полученный от пользователя.
            byte[] decripted;

            try
            {
                // Расшифровать токен.
                decripted = _jwt.DecryptToBytes(accessToken);
            }
            catch (Exception ex)
            {
                // Токен не валиден.
                throw new BadRequestException("Токен не валиден", ex);
            }

            ServerAccessToken bearerToken;
            try
            {
                using (var mem = new MemoryStream(decripted, 0, decripted.Length, false, true))
                {
                    bearerToken = ProtoBuf.Serializer.Deserialize<ServerAccessToken>(mem);
                }
            }
            catch (Exception ex)
            {
                // Токен не валиден.
                throw new BadRequestException("Токен не валиден", ex);
            }
            
            Debug.Assert(bearerToken.ClaimsPrincipal != null);

            if (DateTime.Now < bearerToken.Validity)
            // Токен валиден.
            {
                // Безусловная авторизация.

                try
                {
                    using (var mem = new MemoryStream(bearerToken.ClaimsPrincipal, 0, bearerToken.ClaimsPrincipal.Length, false, true))
                    {
                        using (var breader = new BinaryReader(mem, Encoding.UTF8, true))
                        {
                            var user = new ClaimsPrincipal(breader);
                            lock (_userLock)
                            {
                                User = user;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Токен не валиден.
                    throw new BadRequestException("Токен не валиден", ex);
                }
            }
            else
            {
                throw new BadRequestException("Токен истёк");
            }
        }

        internal void SignOut()
        {
            lock (_userLock)
            {
                User = CreateUnauthorizedUser();
            }
        }

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

        /// <summary>
        /// Потокобезопасно производит авторизацию текущего соединения.
        /// </summary>
        /// <param name="userId">Идентификатор пользователя который будет пазначен текущему контексту.</param>
        private void InnerAuthorize(string name)
        {
            // Функцию могут вызвать из нескольких потоков.
            //lock (_syncObj)
            //{
            //    if (!IsAuthorized)
            //    {
            //        // Авторизуем контекст пользователя.
            //        UserId = userId;
            //        IsAuthorized = true;

            //        // Добавляем соединение в словарь.
            //        UserConnections = AddConnection(userId);
            //    }
            //    else
            //        throw new BadRequestException($"You are already authorized as 'UserId: {UserId}'", StatusCode.BadRequest);
            //}
        }

        ///// <summary>
        ///// Потокобезопасно добавляет текущее соединение в словарь или создаёт новый словарь.
        ///// </summary>
        //private UserConnections AddConnection(int userId)
        //{
        //    do
        //    {
        //        // Берем существующую структуру или создаем новую.
        //        UserConnections userConnections = Listener.Connections.GetOrAdd(userId, uid => new UserConnections(uid));

        //        // Может случиться так что мы взяли существующую коллекцию но её удаляют из словаря в текущий момент.
        //        lock (userConnections.SyncRoot) // Захватить эксклюзивный доступ.
        //        {
        //            // Если коллекцию еще не удалили из словаря то можем безопасно добавить в неё соединение.
        //            if (!userConnections.IsDestroyed)
        //            {
        //                userConnections.Add(this);
        //                return userConnections;
        //            }
        //        }
        //    } while (true);
        //}

        //protected override void BeforeInvokePrepareController(Controller controller)
        //{
        //    var serverController = (ServerController)controller;
        //    serverController.Context = this;
        //    //serverController.Listener = Listener;
        //}

        public ServerSideConnection[] GetConnectionsExceptSelf()
        {
            return Listener.GetConnectionsExcept(this);
        }

        /// <summary>
        /// Проверяет доступность запрашиваемого метода пользователем.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        protected override bool InvokeMethodPermissionCheck(MethodInfo method, Type controllerType, out IActionResult permissionError)
        {
            //// Проверить доступен ли метод пользователю.
            //if (IsAuthorized)
            //    return;

            // Разрешить если метод помечен как разрешенный для не авторизованных пользователей.
            if (Attribute.IsDefined(method, typeof(AllowAnonymousAttribute)))
            {
                permissionError = null;
                return true;
            }

            // Разрешить если контроллер помечен как разрешенный для не акторизованных пользователей.
            if (Attribute.IsDefined(controllerType, typeof(AllowAnonymousAttribute)))
            {
                permissionError = null;
                return true;
            }

            permissionError = new UnauthorizedResult("This action requires user authentication.", StatusCode.Unauthorized);
            return false;
        }

        private protected override void BeforeInvokeController(Controller controller)
        {
            var serverController = (ServerController)controller;
            serverController.Context = this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected override T InnerGetProxy<T>()
        {
            return GetProxy<T>();
        }
    }
}
