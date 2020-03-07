using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;

namespace Server
{
    [AllowAnonymous]
    public class AccountController : ServerController
    {
        public AccountController()
        {

        }

       
        public string Login(string name, string password)
        {
            BearerToken bearerToken = Authenticate();
            return "";
            //bearerToken.Key
            //return accessToken;
        }
    }
}
