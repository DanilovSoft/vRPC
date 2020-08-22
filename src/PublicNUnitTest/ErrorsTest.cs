using DanilovSoft.vRPC;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicXUnitTest
{
    public class ErrorsTest
    {
        [Test]
        public async Task ParseErrorTest()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();

            var ws = new DanilovSoft.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri("ws://localhost:1234"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""method"": ""foobar, ""params"": ""bar"", ""baz]"), WebSocketMessageType.Text, true, default);

            byte[] buf = new byte[1024];
            var m = await ws.ReceiveAsync(buf, default);
            
            //string response = Encoding.UTF8.GetString(buf.AsSpan(0, m.Count));

            //var dto = JsonSerializer.Deserialize<ErrorResponse>(response);

            Assert.AreEqual("Parse error (-32700)", m.CloseStatusDescription);
        }

        [Test]
        public async Task MethodNotFoundTest()
        {
            VRpcListener listener = new VRpcListener(IPAddress.Any);
            listener.Start();

            var ws = new DanilovSoft.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{listener.Port}"), default);
            await ws.SendAsync(Encoding.UTF8.GetBytes(@"{""jsonrpc"": ""2.0"", ""method"": ""foobar"", ""id"": ""1""}"), WebSocketMessageType.Text, true, default);

            byte[] buf = new byte[1024];
            var m = await ws.ReceiveAsync(buf, default);

            string response = Encoding.UTF8.GetString(buf.AsSpan(0, m.Count));

            //var dto = JsonSerializer.Deserialize<ErrorResponse>(response);

            Assert.AreEqual("Parse error (-32700)", m.CloseStatusDescription);
        }
    }

    public class ErrorResponse
    {
        public string Jsonrpc { get; set; }
        public ErrorDto Error { get; set; }
        public int? id { get; set; }
    }

    public class ErrorDto
    {
        public int Code { get; set; }
        public string Message { get; set; }
    }
}
