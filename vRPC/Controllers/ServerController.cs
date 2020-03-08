using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
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

        public BearerToken CreateAccessToken(ClaimsPrincipal claimsPrincipal)
        {
            if (claimsPrincipal == null)
                throw new ArgumentNullException(nameof(claimsPrincipal));

            return Context.CreateAccessToken(claimsPrincipal);
        }

        public void SignOut() => Context.SignOut();
    }

    //public static class ControllerExtensions
    //{
    //    [SuppressMessage("Design", "CA1062:Проверить аргументы или открытые методы", Justification = "<Ожидание>")]
    //    public static void SignOut(this ServerController controller)
    //    {
    //        controller.Context.SignOut();
    //    }
    //}
}
