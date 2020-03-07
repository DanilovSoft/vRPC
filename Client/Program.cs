using DanilovSoft.vRPC;
using System;
using System.Threading.Tasks;

namespace Client
{
    class Program
    {
        static void Main()
        {
            var client = new RpcClient("localhost", 1234, false, false);
            client.Connect();
            var account = client.GetProxy<IAccountController>(out var decorator);
            BearerToken accessToken = account.GetToken("user", "p@$$word");
            client.Authenticate(accessToken.AccessToken);
            //account.Logout();
        }
    }

    public interface IAccountController
    {
        BearerToken GetToken(string name, string password);
        void Logout();
    }
}
