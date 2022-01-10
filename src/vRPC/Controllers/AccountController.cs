using System;
using System.Diagnostics;
using System.Reflection;

namespace DanilovSoft.vRPC.Controllers
{
    [AllowAnonymous]
    internal sealed class AccountController : RpcController
    {
        public static readonly MethodInfo SignInMethod = typeof(AccountController).GetMethod(nameof(SignIn))!;
        public static readonly MethodInfo SignOutMethod = typeof(AccountController).GetMethod(nameof(SignOut))!;

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
            throw new NotImplementedException();
            //return Connection.SignIn(accessToken);
        }

        public IActionResult SignOut()
        {
            throw new NotImplementedException();
        }
    }
}
