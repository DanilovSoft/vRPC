using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Decorator;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp
{
    class Program
    {
        static async Task Main()
        {
            using var listener = new VRpcListener(IPAddress.Any, 1002);
            listener.Start();
            using var client = new VRpcClient("127.0.0.1", 1002, false, true);
            client.Connect();

            var controller = client.GetProxy<IMyServer>();
            try
            {
                await controller.TestNotification();
            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }

            listener.Shutdown(TimeSpan.FromSeconds(2));
        }
    }

    public interface IMyServer
    {
        [Notification]
        ValueTask TestNotification();
        
        int TcpData(int connectionData);
        Task<int> TcpDataAsync(int connectionData, byte[] data);
    }
}
