using DanilovSoft.vRPC;
using DanilovSoft.vRPC.JsonRpc;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp
{
    public class StubController : ServerController
    {
        public void Subtract(int x, int y) { }
    }

    class Program
    {
        static void Main()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();
            listener.ClientConnected += Listener_ClientConnected;


            //VRpcClient client = new VRpcClient("localhost", 1234, false, allowAutoConnect: true);
            //IPing p = client.GetProxy<IPing>();

            //while (true)
            //{
            //    p.Ping("test", 123);
            //    Thread.Sleep(1000);
            //}

            //var methods = new InvokeActionsDictionary(new Dictionary<string, Type> { ["Stub"] = typeof(StubController) });

            //string json = @"{""jsonrpc"": ""2.0"", ""method"": ""Stub/Subtract"", ""params"": [42, 23], ""id"": 1}";

            //JsonRpcSerializer.TryDeserialize(Encoding.UTF8.GetBytes(json), methods, out JsonRequest result, out var _);

            Thread.Sleep(-1);
        }

        private static async void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            //await Task.Delay(2000);
            //e.Connection.GetProxy<ITest>().Echo("qwerty", 123);
        }
    }

    [AllowAnonymous]
    public class TestController : ServerController
    {
        public void Message(string msg)
        {
            Console.WriteLine(msg);
        }

        //public int Add(int x, int y)
        //{
        //    return x + y;
        //}
    }

    [JsonRpc]
    public interface ITest
    {
        string Echo(string msg, int tel);
    }
}
