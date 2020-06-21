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
            using var listener = new VRpcListener(IPAddress.Any, 65125);
            listener.Start();
            using var client = new VRpcClient("localhost", 65125, false, true);
            client.Connect();

            var controller = client.GetProxy<IMyServer>();
            try
            {
                while (true)
                {
                    controller.VoidOneArg(123);
                }
            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }

            listener.Shutdown(TimeSpan.FromSeconds(2));
        }
    }

    [ControllerContract("Benchmark")]
    public interface IMyServer
    {
        [TcpNoDelay]
        void VoidOneArg(int n);

        //[Notification]
        void TestNotification();
        
        int TcpData(int connectionData);
        Task<int> TcpDataAsync(int connectionData, byte[] data);
    }
}
