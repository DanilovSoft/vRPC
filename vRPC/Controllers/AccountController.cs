using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DanilovSoft.vRPC.Controllers
{
    [AllowAnonymous]
    internal sealed class AccountController : ServerController
    {
        public static readonly MethodInfo SignInMethod = typeof(AccountController).GetMethod(nameof(SignIn));
        public static readonly MethodInfo SignOutMethod = typeof(AccountController).GetMethod(nameof(SignOut));

#if DEBUG
        static AccountController()
        {
            Debug.Assert(SignInMethod != null);
            Debug.Assert(SignOutMethod != null);
        }
#endif
        public AccountController()
        {

        }

        public IActionResult SignIn(AccessToken accessToken)
        {
            return Context.SignIn(accessToken);
        }

        //public IActionResult SignOut()
        //{
        //    Context.SignOut();
        //    return Ok();
        //}
    }
}
