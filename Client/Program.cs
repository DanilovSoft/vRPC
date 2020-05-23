using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Decorator;
using Newtonsoft.Json;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    class Program
    {
        static async Task Main()
        {
            var client = new RpcClient("127.0.0.1", 1234, false, true);
            client.Connect();

            var account = client.GetProxy<IAccountController>();
            var admin = client.GetProxy<IAdmin>();
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
