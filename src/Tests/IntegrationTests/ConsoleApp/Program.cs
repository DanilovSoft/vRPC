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
            listener.ClientConnected += Listener_ClientConnected;
            Thread.Sleep(-1);

            //VRpcClient client = new VRpcClient("localhost", 1234, false, allowAutoConnect: true);
            //IPing p = client.GetProxy<IPing>();

            //while (true)
            //{
            //    p.Ping("test", 123);
            //    Thread.Sleep(1000);
            //}
        }

        private static void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            //e.Connection.GetProxy<ITest>().Echo("qwerty");
        }
    }

    public class TestController : ServerController
    {
        public void Message(string msg)
        {
            Console.WriteLine(msg);
        }

        public int Add(int x, int y)
        {
            return x + y;
        }
    }

    [JsonRpcCompatible]
    public interface ITest
    {
        string Echo(string msg);
    }
}
