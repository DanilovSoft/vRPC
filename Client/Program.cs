using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Decorator;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace Client
{
    class Program
    {
        static void Main()
        {
            //var date = DateTime.Now;
            //BearerToken bt1 = new BearerToken(new byte[] { 1,2,3 }, date);
            //BearerToken bt2 = new BearerToken(new byte[] { 1,2,3 }, date);

            //var jj = System.Text.Json.JsonSerializer.Serialize(bt1);
            //var obj2 = System.Text.Json.JsonSerializer.Deserialize<BearerToken>(jj);

            //string j = JsonConvert.SerializeObject(bt1, Formatting.Indented);
            //var obj = JsonConvert.DeserializeObject<BearerToken>(j);

            var client = new RpcClient("localhost", 1234, false, true);

            //if (string.IsNullOrEmpty(Settings.Default.AccessToken))
            {
                client.Connect();
                var account = client.GetProxy<IAccountController>();
                var admin = client.GetProxy<IAdmin>();
                BearerToken bearerToken = account.GetToken("user", "p@$$word");
                
                Settings.Default.AccessToken = Convert.ToBase64String(bearerToken.AccessToken);
                Settings.Default.Save();

                client.SignInAsync(bearerToken.AccessToken).GetAwaiter().GetResult();
                admin.TestAdmin();
                client.SignOutAsync().GetAwaiter().GetResult();
                admin.TestAdmin();
            }
            //else
            //{
            //    var accessToken = Convert.FromBase64String(Settings.Default.AccessToken);
            //    client.SignIn(accessToken);

            //    Settings.Default.AccessToken = null;
            //    Settings.Default.Save();
            //}
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
