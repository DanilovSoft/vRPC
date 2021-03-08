using DanilovSoft.vRPC.Decorator;
using DanilovSoft.vRPC.Source;
using DanilovSoft.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
    public sealed class ServerSideConnection : VrpcManagedConnection, IGetProxy
    {
        // TODO: вынести в параметры.
        private const string _passPhrase = "Pas5pr@se";        // Может быть любой строкой.
        private const string _initVector = "@1B2c3D4e5F6g7H8"; // Должно быть 16 байт.
        private const string _salt = "M6PgwzAnHy02Jv8z5FPIoOn5NeJP7bx7";

        internal static readonly ServerConcurrentDictionary<MethodInfo, RequestMethodMeta> _methodDict = new();
        private readonly ProxyCache _proxyCache = new();

        private RijndaelEnhanced? _jwt;
        private RijndaelEnhanced Jwt => LazyInitializer.EnsureInitialized(ref _jwt, () => new RijndaelEnhanced(_passPhrase, _initVector, 8, 16, 256, _salt, 1000));
        private volatile ClaimsPrincipal _user;
        public sealed override bool IsAuthenticated => true;
        /// <summary>
        /// Пользователь ассоциированный с текущим соединением.
        /// </summary>
        public ClaimsPrincipal User => _user;
       
        /// <summary>
        /// Сервер который принял текущее соединение.
        /// </summary>
        public VRpcListener Listener { get; }

        // ctor.
        // Только Listener может создать этот класс.
        internal ServerSideConnection(ManagedWebSocket clientConnection, ServiceProvider serviceProvider, VRpcListener listener)
            : base(clientConnection, isServer: true, serviceProvider, listener.InvokeActions)
        {
            Listener = listener;

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
            T? proxy = GetProxyDecorator<T>().Proxy;
            Debug.Assert(proxy != null);
            return proxy;
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
                    encryptedToken = Jwt.EncryptToBytes(serializedTmpBuf.AsSpan(0, (int)mem.Length));
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
                decripted = Jwt.DecryptToBytes(accessToken);
            }
            catch (Exception)
            {
                return new InvalidParamsResult("Токен не валиден");
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
                return new InvalidParamsResult("Токен не валиден");
            }

            return SignIn(bearerToken);
        }

        private IActionResult SignIn(ServerAccessToken bearerToken)
        {
            Debug.Assert(bearerToken.ClaimsPrincipal != null);

            ClaimsPrincipal user;
            if (DateTime.Now < bearerToken.Validity)
            // Токен валиден.
            {
                using (var mem = new MemoryStream(bearerToken.ClaimsPrincipal, 0, bearerToken.ClaimsPrincipal.Length, writable: false, publiclyVisible: true))
                using (var breader = new BinaryReader(mem, Encoding.UTF8, leaveOpen: true))
                {
                    try
                    {
                        user = new ClaimsPrincipal(breader);
                    }
                    catch (EndOfStreamException)
                    {
                        return new InvalidParamsResult("Аутентификация не работает на .NET Framework из-за бага");
                    }
                    catch (Exception)
                    {
                        return new InvalidParamsResult("Токен не валиден");
                    }
                }
            }
            else
            {
                return new InvalidParamsResult("Токен истёк");
            }

            // Эта строка фактически атомарно аутентифицирует соединение для всех последующих запросов.
            _user = user;

            Listener.OnConnectionAuthenticated(this, user);
            return new OkResult();
        }

        /// <summary>
        /// Сбрасывает аутентификацию соединения в изначальное состояние.
        /// </summary>
        public void SignOut()
        {
            // volatile копия.
            ClaimsPrincipal user = _user;

            if (user.Identity?.IsAuthenticated == true)
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
        private protected sealed override bool ActionPermissionCheck(ControllerMethodMeta actionMeta, [NotNullWhen(false)] out IActionResult? permissionError, out ClaimsPrincipal user)
        {
            Debug.Assert(actionMeta != null);

            // Скопируем пользователя что-бы не мог измениться в пределах запроса.
            user = _user;

            // 1. Проверить доступен ли метод пользователю.
            if (user.Identity?.IsAuthenticated == true)
            {
                permissionError = null;
                return true;
            }

            // 2. Разрешить если весь контроллер помечен как разрешенный для не авторизованных пользователей.
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

            permissionError = new UnauthorizedErrorResult($"Action '{actionMeta.MethodFullName}' requires user authentication.");
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected sealed override T InnerGetProxy<T>() => GetProxy<T>();

        #region Call Helpers

        /// <exception cref="VRpcException"/>
        public void Call(string controllerName, string actionName, params object[] args)
        {
            var method = new RequestMethodMeta(controllerName, actionName, typeof(VoidStruct), false);
            var request = new VRequest<VoidStruct>(method, args);
            if (TrySendRequest<VoidStruct>(request, out var error))
            {
                request.Task.GetAwaiter().GetResult();
            }
            else
                error.GetAwaiter().GetResult();
        }

        /// <exception cref="VRpcException"/>
        public Task CallAsync(string controllerName, string actionName, params object[] args)
        {
            var method = new RequestMethodMeta(controllerName, actionName, typeof(VoidStruct), false);
            var request = new VRequest<VoidStruct>(method, args);
            if (TrySendRequest<VoidStruct>(request, out var error))
            {
                return request.Task;
            }
            else
                return error;
        }

        /// <exception cref="VRpcException"/>
        [return: MaybeNull]
        public TResult Call<TResult>(string controllerName, string actionName, params object[] args)
        {
            var method = new RequestMethodMeta(controllerName, actionName, typeof(TResult), false);
            var request = new VRequest<TResult>(method, args);
            if (TrySendRequest<TResult>(request, out var error))
            {
                return request.Task.GetAwaiter().GetResult();
            }
            else
                return error.GetAwaiter().GetResult();
        }

        /// <exception cref="VRpcException"/>
        public Task<TResult> CallAsync<TResult>(string controllerName, string actionName, params object[] args)
        {
            var method = new RequestMethodMeta(controllerName, actionName, typeof(TResult), false);
            var request = new VRequest<TResult>(method, args);
            if (TrySendRequest<TResult>(request, out var error))
            {
                return request.Task;
            }
            else
                return error;
        }

        #endregion
    }
}
