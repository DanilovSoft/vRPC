using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class ServerController : Controller
    {
        /// <summary>
        /// Контекст подключения.
        /// </summary>
        public ServerSideConnection Context { get; private set; }
        /// <summary>
        /// Пользователь ассоциированный с текущим запросом.
        /// </summary>
        public ClaimsPrincipal User { get; private set; }

        // Должен быть пустой конструктор для наследников.
        public ServerController()
        {
            
        }

        internal override void BeforeInvokeController(ManagedConnection connection, ClaimsPrincipal user)
        {
            Context = connection as ServerSideConnection;
            Debug.Assert(Context != null);
            User = user;
        }

        public BearerToken CreateAccessToken(ClaimsPrincipal claimsPrincipal, TimeSpan validTime)
        {
            if (claimsPrincipal == null)
                throw new ArgumentNullException(nameof(claimsPrincipal));

            if (validTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(validTime));

            return Context.CreateAccessToken(claimsPrincipal, validTime);
        }

        /// <summary>
        /// Сбрасывает аутентификацию соединения в изначальное состояние.
        /// </summary>
        public void SignOut() => Context.SignOut();
    }
}
