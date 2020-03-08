using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Decorator;
using System;
using System.Threading.Tasks;

namespace Client
{
    class Program
    {
        static void Main()
        {
            var client = new RpcClient("localhost", 1234, false, true);

            if (string.IsNullOrEmpty(Settings.Default.AccessToken))
            {
                client.Connect();
                var account = client.GetProxy<IAccountController>();
                var admin = client.GetProxy<IAdmin>();
                BearerToken bearerToken = account.GetToken("user", "p@$$word");
                
                Settings.Default.AccessToken = Convert.ToBase64String(bearerToken.AccessToken);
                Settings.Default.Save();

                client.SignIn(bearerToken.AccessToken);
                admin.TestAdmin();
                client.SignOut();
            }
            else
            {
                var accessToken = Convert.FromBase64String(Settings.Default.AccessToken);
                client.SignIn(accessToken);

                Settings.Default.AccessToken = null;
                Settings.Default.Save();
            }
        }
    }

    public interface IAccountController
    {
        BearerToken GetToken(string name, string password);
    }

    public interface IAdmin
    {
        void TestAdmin();
    }
}
