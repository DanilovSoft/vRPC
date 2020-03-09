using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text;

namespace Server
{
    [AllowAnonymous]
    public class AccountController : ServerController
    {
        public AccountController()
        {
            
        }

        [ProducesProtoBuf]
        public BearerToken GetToken(string userName, string password)
        {
            // создаем один claim
            var claims = new List<Claim>
            {
                new Claim(ClaimsIdentity.DefaultNameClaimType, userName)
            };
            
            // Аутентифицированный пользователь с именем.
            var idIdentity = new ClaimsIdentity(claims, "AccessToken");

            // Аутентифицированный пользователь и его клеймы (роли и тп.)
            var user = new ClaimsPrincipal(idIdentity);

            var token = CreateAccessToken(user, TimeSpan.FromDays(2));
            return token;
        }

        public void Logout()
        {
            SignOut();
        }
    }
}
