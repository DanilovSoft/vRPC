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
        public async Task ParseErrorTest()
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
        public async Task MethodNotFoundTest()
        {
            var listener = VRpcListener.StartNew(IPAddress.Any);
            var client = new VRpcClient("localhost", listener.Port, false, true);

            await client.ConnectAsync();

            try
            {
                client.GetProxy<IServerTestController>().NotExistedMethod();
            }
            catch (VRpcMethodNotFoundException)
            {
                Assert.Pass();
            }
            Assert.Fail();
        }
    }
}
