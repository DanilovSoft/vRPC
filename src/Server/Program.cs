using DanilovSoft.vRPC;
using System;
using System.Diagnostics;
using System.Net;

namespace Server
{
    public interface IHome
    {
        [Notification]
        void Test();
    }

    class Program
    {
        static void Main()
        {
            var listener = new VRpcListener(IPAddress.Any, 1234);
            listener.ClientConnected += Listener_ClientConnected;
            listener.ClientAuthenticated += Listener_ClientAuthenticated;
            listener.ClientSignedOut += Listener_ClientSignedOut;
            listener.RunAsync().Wait();
        }

        private static void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            //e.Connection.GetProxy<IHome>().Test();
        }

        private static void Listener_ClientSignedOut(object sender, ClientSignedOutEventArgs e)
        {
            Debug.Assert(e.Connection.User.Identity.IsAuthenticated == false);
            Console.WriteLine($"Разлогинился: '{e.User.Identity.Name}'");
        }

        private static void Listener_ClientAuthenticated(object sender, ClientAuthenticatedEventArgs e)
        {
            Console.WriteLine($"Аутентифицирован: '{e.User.Identity.Name}'");
        }
    }
}
