using DanilovSoft.vRPC;
using System;
using System.Net;
using System.Threading;

namespace ConsoleApp
{
    class Program
    {
        static void Main()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();

            VRpcClient client = new VRpcClient("localhost", 1234, false, allowAutoConnect: true);
            IPing p = client.GetProxy<IPing>();

            while (true)
            {
                p.Ping("test", 123);
                Thread.Sleep(1000);
            }
        }
    }

    public class TestController : ClientController
    {
        public void Message(string msg)
        {
            Console.WriteLine(msg);
        }
    }

    [JsonRpcCompatible]
    public interface IPing
    {
        void Ping(string msg, int n);
    }
}
