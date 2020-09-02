using DanilovSoft.vRPC;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XUnitTest;

namespace PublicXUnitTest
{
    public class ErrorsTest
    {
        [Test]
        public async Task TestParseError()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            var ws = new DanilovSoft.WebSockets.ClientWebSocket();

            await ws.ConnectAsync(new Uri($"ws://localhost:{listener.Port}"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""method"": ""foobar, ""params"": ""bar"", ""baz]"), WebSocketMessageType.Text, true, default);
            
            var buf = new byte[1024];
            var m = await ws.ReceiveAsync(buf, default);
            
            Assert.AreEqual("Parse error (-32700)", m.CloseStatusDescription);
        }

        [Test]
        public void TestMethodNotFound()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            var client = new VRpcClient("localhost", listener.Port, false, true);
            try
            {
                client.GetProxy<IServerTestController>().JNotExistedMethod();
            }
            catch (VRpcMethodNotFoundException)
            {
                return;
            }
            Assert.Fail();
        }

        [Test]
        public void TestInternalError()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            var client = new VRpcClient("localhost", listener.Port, false, true);
            try
            {
                client.GetProxy<IServerTestController>().JTestInternalError();
            }
            catch (VRpcInternalErrorException)
            {
                return;
            }
            Assert.Fail();
        }

        [Test]
        public async Task TestInvalidRequest()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            var ws = new DanilovSoft.WebSockets.ClientWebSocket();

            await ws.ConnectAsync(new Uri($"ws://localhost:{listener.Port}"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""method"": 1, ""params"": ""bar""}"), WebSocketMessageType.Text, true, default);

            var buf = new byte[1024];
            var m = await ws.ReceiveAsync(buf, default);

            Assert.AreEqual("Parse error (-32700)", m.CloseStatusDescription);
        }
    }
}
