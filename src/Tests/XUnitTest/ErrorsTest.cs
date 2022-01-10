using DanilovSoft.vRPC;
using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using XUnitTest;

namespace PublicXUnitTest
{
    public class ErrorsTest
    {
        [Fact]
        public async Task TestParseError()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);
            var ws = new DanilovSoft.WebSockets.ClientWebSocket();

            await ws.ConnectAsync(new Uri($"ws://localhost:{listener.Port}"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""method"": ""foobar, ""params"": ""bar"", ""baz]"), 
                WebSocketMessageType.Text, true, default);
            
            var buf = new byte[1024];
            var m = await ws.ReceiveAsync(buf, default);
            
            Assert.Equal("Parse error (-32700)", ws.CloseStatusDescription);
        }

        [Fact]
        public void TestMethodNotFound()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);
            var client = new VRpcClient("localhost", listener.Port, false, true);
            try
            {
                client.GetProxy<IServerTestController>().JNotExistedMethod();
            }
            catch (VRpcMethodNotFoundException)
            {
                return;
            }
            Assert.True(false);
        }

        [Fact]
        public void TestInternalError()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);
            var client = new VRpcClient("localhost", listener.Port, false, true);
            try
            {
                client.GetProxy<IServerTestController>().JTestInternalError();
            }
            catch (VRpcInternalErrorException)
            {
                return;
            }
            Assert.True(false);
        }

        [Fact]
        public async Task TestInvalidRequest()
        {
            using var listener = VRpcListener.StartNew(IPAddress.Any);
            var ws = new DanilovSoft.WebSockets.ClientWebSocket();

            await ws.ConnectAsync(new Uri($"ws://localhost:{listener.Port}"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""method"": ""foobar, ""params"": ""bar"", ""baz]"), 
                WebSocketMessageType.Text, true, default);

            var buf = new byte[1024];
            var m = ws.ReceiveExAsync(buf, default).AsTask().GetAwaiter().GetResult();

            if (m.MessageType == WebSocketMessageType.Text)
            {
                string wtf = Encoding.UTF8.GetString(buf, 0, m.Count);
            }

            Assert.Equal("Parse error (-32700)", ws.CloseStatusDescription);
        }
    }
}
