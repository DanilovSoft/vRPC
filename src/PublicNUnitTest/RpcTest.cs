using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.TestExceptionThrow("проверка");
            }
            catch (VRpcBadRequestException ex)
            {
                Assert.AreEqual("проверка", ex.Message);
            }
        }

        [Test]
        public void TestException()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.TestException("проверка");
            }
            catch (VRpcBadRequestException ex)
            {
                Assert.AreEqual("проверка", ex.Message);
            }
        }

        [Test]
        public void TestDelayVoid()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, false);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            var sw = Stopwatch.StartNew();
            iface.TestDelay();
            Assert.True(sw.ElapsedMilliseconds >= 500);
        }

        [Test]
        public async Task TestAsyncVoid()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, false);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            var sw = Stopwatch.StartNew();
            await iface.Test2Async();
            Assert.True(sw.ElapsedMilliseconds >= 500);
        }

        [Test]
        public void TestSumResult()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();
            
            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum(1, 2);
            Assert.AreEqual(3, sum);
        }

        [Test]
        public void TestSumValueTask()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum2(1, 2);
            Assert.AreEqual(3, sum);
        }

        [Test]
        public void TestStringResult()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetString();
            Assert.AreEqual("OK", value);
        }

        [Test]
        public void TestNullStringResult()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetNullString();
            Assert.Null(value);
        }

        [Test]
        public async Task TestNullStringAsync()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = await iface.GetNullStringAsync();
            Assert.Null(value);
        }

        [Test]
        public async Task TestSumAsync()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = await iface.GetSumAsync(1, 2);
            Assert.AreEqual(3, sum);
        }

        [Test]
        public async Task TestNotificationAsync()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            await iface.NotifyAsync(123);
        }

        [Test]
        public void TestNotification()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            iface.Notify(123);
        }

        [Test]
        public void TestCallback()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeCallback("qwerty");
            Assert.AreEqual("qwerty", selfEcho);
        }

        [Test]
        public void TestAsyncCallback()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeAsyncCallback("qwerty");
            Assert.AreEqual("qwerty", selfEcho);
        }

        [Test]
        public void TestNotificationCallback()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var mre = new ManualResetEventSlim(false);
            cli.ConfigureService(x => x.AddSingleton(mre));
            var iface = cli.GetProxy<IServerTestController>();

            iface.NotifyCallback(123);

            Assert.True(mre.Wait(30_000));
        }

        [Test]
        public void TestMethodNotFound()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.NotExistedMethod();
                Assert.True(false);
            }
            catch (VRpcException)
            {
                Assert.Pass();
            }
        }
    }
}
