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
        private static readonly ServerConcurrentDictionary<MethodInfo, RequestMeta> _interfaceMethodsInfo = new ServerConcurrentDictionary<MethodInfo, RequestMeta>();
        private readonly ProxyCache _proxyCache = new ProxyCache();
        private readonly RijndaelEnhanced _jwt;
        private volatile ClaimsPrincipal _user;
        public override bool IsAuthenticated => true;
        /// <summary>
        /// Пользователь ассоциированный с текущим соединением.
        /// </summary>
        public ClaimsPrincipal User => _user;
        private protected override IConcurrentDictionary<MethodInfo, RequestMeta> InterfaceMethods => _interfaceMethodsInfo;

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
            _user = CreateUnauthorizedUser();
        }

        private static ClaimsPrincipal CreateUnauthorizedUser()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// Полученный экземпляр можно привести к типу <see cref="ServerInterfaceProxy"/>.
        /// Метод является шорткатом для <see cref="GetProxyDecorator"/>
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public T GetProxy<T>() where T : class
        {
            return GetProxyDecorator<T>().Proxy;
        }

        /// <summary>
        /// Создает прокси к методам удалённой стороны на основе интерфейса. Повторное обращение вернет экземпляр из кэша.
        /// </summary>
        /// <typeparam name="T">Интерфейс.</typeparam>
        public ServerInterfaceProxy<T> GetProxyDecorator<T>() where T : class
        {
            return _proxyCache.GetProxyDecorator<T>(this);
        }

        internal BearerToken CreateAccessToken(ClaimsPrincipal claimsPrincipal, TimeSpan validTime)
        {
            byte[] encryptedToken;
            using (var stream = GlobalVars.RecyclableMemory.GetStream("claims-principal", 32))
            {
                using (var bwriter = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    claimsPrincipal.WriteTo(bwriter);
                }
                byte[] serializedClaims = stream.ToArray();

                DateTime validity = DateTime.Now + validTime;
                var serverBearer = new ServerAccessToken(serializedClaims, validity);

                using (var mem = GlobalVars.RecyclableMemory.GetStream())
                {
                    ProtoBuf.Serializer.Serialize(mem, serverBearer);
                    byte[] serializedTmpBuf = mem.GetBuffer();

                    // Закриптовать.
                    encryptedToken = _jwt.EncryptToBytes(serializedTmpBuf.AsSpan(0, (int)mem.Length));
                }

                var token = new BearerToken(encryptedToken, validity);
                return token;
            }
        }

        /// <summary>
        /// Производит аутентификацию текущего подключения.
        /// </summary>
        internal IActionResult SignIn(AccessToken accessToken)
        {
            // Расшифрованный токен полученный от пользователя.
            byte[] decripted;

            try
            {
                // Расшифровать токен.
                decripted = _jwt.DecryptToBytes(accessToken);
            }
            catch (Exception)
            {
                return new BadRequestResult("Токен не валиден");
            }

            ServerAccessToken bearerToken;
            try
            {
                using (var mem = new MemoryStream(decripted, 0, decripted.Length, false, true))
                {
                    bearerToken = ProtoBuf.Serializer.Deserialize<ServerAccessToken>(mem);
                }
            }
            catch (Exception)
            {
                return new BadRequestResult("Токен не валиден");
            }
            
            Debug.Assert(bearerToken.ClaimsPrincipal != null);

            ClaimsPrincipal user;
            if (DateTime.Now < bearerToken.Validity)
            // Токен валиден.
            {
                using (var mem = new MemoryStream(bearerToken.ClaimsPrincipal, 0, bearerToken.ClaimsPrincipal.Length, false, true))
                {
                    using (var breader = new BinaryReader(mem, Encoding.UTF8, true))
                    {
                        try
                        {
                            user = new ClaimsPrincipal(breader);
                        }
                        catch (Exception)
                        {
                            return new BadRequestResult("Токен не валиден");
                        }
                    }
                }
            }
            else
            {
                return new BadRequestResult("Токен истёк");
            }

            // Эта строка фактически атомарно аутентифицирует соединение для всех последующих запросов.
            _user = user;

            Listener.OnConnectionAuthenticated(this, user);
            return new OkResult();
        }

        internal void SignOut()
        {
            // volatile копия.
            var user = _user;
            if (user.Identity.IsAuthenticated)
            {
                _user = CreateUnauthorizedUser();
                Listener.OnUserSignedOut(this, user);
            }
        }

        public ServerSideConnection[] GetConnectionsExceptSelf()
        {
            return Listener.GetConnectionsExcept(this);
        }

        /// <summary>
        /// Проверяет доступность запрашиваемого метода пользователем.
        /// </summary>
        /// <exception cref="BadRequestException"/>
        private protected override bool ActionPermissionCheck(ControllerActionMeta actionMeta, out IActionResult permissionError, out ClaimsPrincipal user)
        {
            // Скопируем пользователя что-бы не мог измениться в пределах запроса.
            user = _user;

            // 1. Проверить доступен ли метод пользователю.
            if (user.Identity.IsAuthenticated)
            {
                permissionError = null;
                return true;
            }

            // 2. Разрешить если контроллер помечен как разрешенный для не акторизованных пользователей.
            if (Attribute.IsDefined(actionMeta.ControllerType, typeof(AllowAnonymousAttribute)))
            {
                permissionError = null;
                return true;
            }

            // 3. Разрешить если метод помечен как разрешенный для не авторизованных пользователей.
            if (Attribute.IsDefined(actionMeta.TargetMethod, typeof(AllowAnonymousAttribute)))
            {
                permissionError = null;
                return true;
            }

            permissionError = new UnauthorizedResult($"Action '{actionMeta.ActionFullName}' requires user authentication.", StatusCode.Unauthorized);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected override T InnerGetProxy<T>() => GetProxy<T>();
    }
}
