using DanilovSoft.vRPC;
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

    [ControllerContract("TestController")]
    public interface IServerTestController
    {
        void TestException(string exceptionMessage);
        [JsonRpc]
        void TestExceptionThrow(string exceptionMessage);
        void TestDelay();
        Task Test2Async();
        [JsonRpc]
        int GetSum(int x1, int x2);
        int GetSum2(int x1, int x2);
        Task<int> GetSumAsync(int x1, int x2);
        Task<string> GetNullStringAsync();
        string GetString();
        string GetNullString();

        [Notification]
        void Notify(int n);
        [Notification]
        ValueTask NotifyAsync(int n);
        [Notification]
        void NotifyCallback(int n);

        string MakeCallback(string msg);
        string MakeAsyncCallback(string msg);
    }

    class Program
    {
        static async Task Main()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);

            var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            while (true)
            {
                int sum = iface.GetSum(1, 2);
            }


            Thread.Sleep(-1);
        }

        private static async void Listener_ClientConnected(object sender, ClientConnectedEventArgs e)
        {
            await Task.Delay(2000);
            e.Connection.GetProxy<ITest>().Echo("qwerty", 123);
        }
    }

    [AllowAnonymous]
    public class TestController : ServerController
    {
        public void Message(string msg)
        {
            Console.WriteLine(msg);
        }

        public int GetSum(int x, int y)
        {
            return x + y;
        }
    }

    [JsonRpc]
    public interface ITest
    {
        string Echo(string msg, int tel);
    }
}
