using DanilovSoft.vRPC;
using System;
using System.Diagnostics;
using System.Net;

namespace Server
{
    class Program
    {
        static void Main()
        {
            var listener = new RpcListener(IPAddress.Any, 1234);
            listener.ClientAuthenticated += Listener_ClientAuthenticated;
            listener.ClientSignedOut += Listener_ClientSignedOut;
            listener.RunAsync().Wait();
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
