using DanilovSoft.vRPC;
using System;
using System.Threading;

namespace ConsoleApp
{
    class Program
    {
        static void Main()
        {
            VRpcClient client = new VRpcClient("localhost", 1234, false, true);
            IPing p = client.GetProxy<IPing>();

            while (true)
            {
                Thread.Sleep(1000);
                p.Ping();
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

    public interface IPing
    {
        void Ping();
    }
}
