using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Claims;

namespace DanilovSoft.vRPC
{
    public abstract class ServerController : Controller
    {
        /// <summary>
        /// Контекст подключения на стороне сервера.
        /// </summary>
        public ServerSideConnection Context { get; internal set; }
        public ClaimsPrincipal User => Context.User;

        // Должен быть пустой конструктор для наследников.
        public ServerController()
        {

        }

        //public void SignIn(AccessToken accessToken) => Context.SignIn(accessToken);

        public BearerToken CreateAccessToken(ClaimsPrincipal claimsPrincipal)
        {
            if (claimsPrincipal == null)
                throw new ArgumentNullException(nameof(claimsPrincipal));

            return Context.CreateAccessToken(claimsPrincipal);
        }

        public void SignOut() => Context.SignOut();
    }
}
