﻿using System;
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
                Assert.Equal("проверка", ex.Message);
            }
        }

        [Fact]
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
                Assert.Equal("проверка", ex.Message);
            }
        }

        [Fact]
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

        [Fact]
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

        [Fact]
        public void TestSumResult()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();
            
            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum(1, 2);
            Assert.Equal(3, sum);
        }

        [Fact]
        public void TestSumValueTask()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = iface.GetSum2(1, 2);
            Assert.Equal(3, sum);
        }

        [Fact]
        public void TestStringResult()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetString();
            Assert.Equal("OK", value);
        }

        [Fact]
        public void TestNullStringResult()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = iface.GetNullString();
            Assert.Null(value);
        }

        [Fact]
        public async Task TestNullStringAsync()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string value = await iface.GetNullStringAsync();
            Assert.Null(value);
        }

        [Fact]
        public async Task TestSumAsync()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            int sum = await iface.GetSumAsync(1, 2);
            Assert.Equal(3, sum);
        }

        [Fact]
        public async Task TestNotificationAsync()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            await iface.NotifyAsync(123);
        }

        [Fact]
        public void TestNotification()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            iface.Notify(123);
        }

        [Fact]
        public void TestCallback()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeCallback("qwerty");
            Assert.Equal("qwerty", selfEcho);
        }

        [Fact]
        public void TestAsyncCallback()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();

            string selfEcho = iface.MakeAsyncCallback("qwerty");
            Assert.Equal("qwerty", selfEcho);
        }

        [Fact]
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

        public void TestMethodNotFound()
        {
            using var listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            using var cli = new VRpcClient("127.0.0.1", listener.Port, false, true);
            var iface = cli.GetProxy<IServerTestController>();
            try
            {
                iface.NotFoundMethod();
                Assert.True(false);
            }
            catch (VRpcException ex)
            {
                
            }
        }
    }
}
