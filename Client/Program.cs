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

            var multipart = client.GetProxy<IMultipart>();
            multipart.Test();
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

    public interface IMultipart
    {
        void Test();
    }
}
