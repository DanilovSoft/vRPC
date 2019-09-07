using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using vRPC;

namespace Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public async Task TestMethod1()
        {
            using (var listener = new RpcListener(IPAddress.Any, 1234))
            {
                listener.ClientKeepAliveInterval = TimeSpan.FromSeconds(6);
                listener.ClientReceiveTimeout = TimeSpan.FromSeconds(6);
                listener.ClientConnected += Listener_ClientConnected;
                listener.Start();

                using (var client = new RpcClient("127.0.0.1", 1234))
                {
                    client.Configure(app => 
                    {
                        app.KeepAliveInterval = TimeSpan.FromSeconds(10);
                        app.ReceiveTimeout = TimeSpan.FromSeconds(30);
                    });

                    await client.ConnectAsync();
                    await client.Completion;
                    Thread.Sleep(-1);
                }
            }
        }

        private async void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            await e.Connection.Completion;
        }
    }
}
