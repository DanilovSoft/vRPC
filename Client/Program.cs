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
        private static AccessToken _accessToken;

        static async Task Main()
        {
            var client = new RpcClient("10.2.2.22", 1234, false, true);

            Console.CancelKeyPress += (s, e) => 
            {
                Console.WriteLine("Closing...");
                e.Cancel = true;
                client.BeginShutdown(TimeSpan.FromSeconds(2), "Пользователь нажал Ctrl+C");
            };

            Console.WriteLine("ConnectExAsync");
            var result = await client.ConnectExAsync();
            
            client.Connected += Client_Connected;
            client.ConfigureAutoAuthentication(() => _accessToken);
            //if (string.IsNullOrEmpty(Settings.Default.AccessToken))
            {
                var account = client.GetProxy<IAccountController>();
                var admin = client.GetProxy<IAdmin>();

                BearerToken bearerToken = account.GetToken("user", "p@$$word");
                
                Settings.Default.AccessToken = Convert.ToBase64String(bearerToken.AccessToken);
                Settings.Default.Save();

                client.SignInAsync(bearerToken.AccessToken).GetAwaiter().GetResult();
                admin.TestAdmin();
                client.SignOutAsync().GetAwaiter().GetResult();
                //admin.TestAdmin();
            }
            //else
            //{
            //    _accessToken = Convert.FromBase64String(Settings.Default.AccessToken);
            //    client.Connect();

            //    Settings.Default.AccessToken = null;
            //    Settings.Default.Save();
            //}
        }

        private static void Client_Connected(object sender, ConnectedEventArgs e)
        {
            
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
