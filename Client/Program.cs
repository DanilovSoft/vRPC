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
            var account = client.GetProxy<IAccountController>();
            string accessToken = account.Login("user", "p@$$word");
            //client.Logout();
        }
    }

    public interface IAccountController
    {
        string Login(string name, string password);
    }
}
