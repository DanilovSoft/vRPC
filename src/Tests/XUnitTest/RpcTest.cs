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
using Xunit;

namespace XUnitTest
{
    public class RpcTest
    {
        [Fact]
        public void TestVoid()
        {
            const int port = 1000;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, false);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            var sw = Stopwatch.StartNew();
            iface.TestDelay();
            Assert.True(sw.ElapsedMilliseconds >= 500);
        }

        [Fact]
        public async Task TestAsyncVoid()
        {
            const int port = 1001;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, false);
            var iface = cli.GetProxy<IServerTestController>();

            cli.Connect();

            var sw = Stopwatch.StartNew();
            await iface.Test2Async();
            Assert.True(sw.ElapsedMilliseconds >= 500);
        }

        [Fact]
        public void TestResult()
        {
            const int port = 1002;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();
            
            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum(1, 2);
            Assert.Equal(3, sum);
        }

        [Fact]
        public void TestSumValueTask()
        {
            const int port = 1003;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum2(1, 2);
            Assert.Equal(3, sum);
        }

        [Fact]
        public void TestStringResult()
        {
            const int port = 1004;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetString();
            Assert.Equal("OK", value);
        }

        [Fact]
        public void TestNullStringResult()
        {
            const int port = 1005;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetNullString();
            Assert.Null(value);
        }

        [Fact]
        public async Task TestNullStringAsync()
        {
            const int port = 1006;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = await iface.GetNullStringAsync();
            Assert.Null(value);
        }

        [Fact]
        public async Task TestSumAsync()
        {
            const int port = 1007;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = await iface.GetSumAsync(1, 2);
            Assert.Equal(3, sum);
        }

        [Fact]
        public async Task TestNotificationAsync()
        {
            const int port = 1008;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            await iface.NotifyAsync(123);
        }

        [Fact]
        public void TestNotification()
        {
            const int port = 1009;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            iface.Notify(123);
        }

        [Fact]
        public void TestCallback()
        {
            const int port = 1010;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeCallback("qwerty");
            Assert.Equal("qwerty", selfEcho);
        }

        [Fact]
        public void TestAsyncCallback()
        {
            const int port = 1011;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeAsyncCallback("qwerty");
            Assert.Equal("qwerty", selfEcho);
        }

        [Fact]
        public void TestNotificationCallback()
        {
            const int port = 1012;

            using var listener = new VRpcListener(IPAddress.Any, port);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", port, false, true);
            var mre = new ManualResetEventSlim(false);
            cli.ConfigureService(x => x.AddSingleton(mre));
            var iface = cli.GetProxy<IServerTestController>();

            iface.NotifyCallback(123);

            Assert.True(mre.Wait(30_000));
        }
    }
}
