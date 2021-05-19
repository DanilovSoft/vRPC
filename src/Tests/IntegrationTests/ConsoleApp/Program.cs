using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InternalConsoleApp
{
    class Program
    {
        static async Task Main()
        {
            //var listener = VRpcListener.StartNew(IPAddress.Any);

            var cli = new VRpcClient(new Uri($"wss://localhost:44343"), allowAutoConnect: false);
            var iface = cli.GetProxy<IServerTestController>();

            //Thread.Sleep(2000);
            cli.Connect();

            while (true)
            {
                try
                {
                    int sum = await iface.GetSumAsync(1, 2);
                }
                catch (Exception ex)
                {
                    return;
                }
            }
        }

        private static async void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            await Task.Delay(2000);
            e.Connection.GetProxy<ITest>().Echo("qwerty", 123);
        }
    }
}
