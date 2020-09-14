using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DanilovSoft.AsyncEx;
using DanilovSoft.vRPC;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace XUnitTest
{
    public class RpcTest
    {
        [Test]
        public void TestExceptionThrow()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);
            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);

            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.TestInternalErrorThrow("проверка");
            }
            catch (VRpcInternalErrorException ex)
            {
                Assert.AreEqual("проверка", ex.Message);
            }
            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestException()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.InvalidParamsResult("проверка");
            }
            catch (VRpcInvalidParamsException ex)
            {
                Assert.AreEqual("проверка", ex.Message);
            }
            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestDelayVoid()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, false);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            var sw = Stopwatch.StartNew();
            iface.TestDelay();
            Assert.True(sw.ElapsedMilliseconds >= 500);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public async Task TestAsyncVoid()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, false);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            var sw = Stopwatch.StartNew();
            await iface.Test2Async();
            Assert.True(sw.ElapsedMilliseconds >= 500);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestSumResult()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum(1, 2);
            Assert.AreEqual(3, sum);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestSumValueTask()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum2(1, 2);
            Assert.AreEqual(3, sum);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestStringResult()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetString();
            Assert.AreEqual("OK", value);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestNullStringResult()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetNullString();
            Assert.Null(value);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public async Task TestNullStringAsync()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = await iface.GetNullStringAsync();
            Assert.Null(value);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public async Task TestSumAsync()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = await iface.GetSumAsync(1, 2);
            Assert.AreEqual(3, sum);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public async Task TestNotificationAsync()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            await iface.NotifyAsync(123);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public async Task TestJNotificationAsync()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            await iface.NotifyAsync(123);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestNotification()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            iface.Notify(123);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestCallback()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeCallback("qwerty");
            Assert.AreEqual("qwerty", selfEcho);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestAsyncCallback()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeAsyncCallback("qwerty");
            Assert.AreEqual("qwerty", selfEcho);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestNotificationCallback()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var mre = new ManualResetEventSlim(false);
            cli.ConfigureService(x => x.AddSingleton(mre));
            var iface = cli.GetProxy<IServerTestController>();

            iface.NotifyCallback(123);

            Assert.True(mre.Wait(30_000), "Превышено время ожидания обратного вызова");

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public void TestJNotificationCallback()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var mre = new ManualResetEventSource<int>();
            cli.ConfigureService(x => x.AddSingleton(mre));
            var iface = cli.GetProxy<IServerTestController>();

            mre.Reset();
            iface.JNotifyCallback(123);

            Assert.True(mre.Wait(TimeSpan.FromSeconds(30), out int n), "Превышено время ожидания обратного вызова");
            Assert.True(n == 123);

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }

        [Test]
        public async Task TestJWorseRequest()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            var ws = new DanilovSoft.WebSockets.ClientWebSocket();

            await ws.ConnectAsync(new Uri($"ws://localhost:{listener.Port}"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""params"": [1,2], ""method"": ""Sum"", ""id"": 1}"), WebSocketMessageType.Text, true, default);

            var buf = new byte[1024];
            var m = await ws.ReceiveAsync(buf, default);

            Assert.Fail();
        }

        [Test]
        public void TestMethodNotFound()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);
            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.NotExistedMethod();
                Assert.True(false);
            }
            catch (VRpcMethodNotFoundException)
            {
                return;
            }
            Assert.Fail();

            cli.Shutdown(TimeSpan.FromSeconds(1), "Unit Test");
        }
    }
}
