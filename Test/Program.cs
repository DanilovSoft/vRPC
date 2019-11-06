using DanilovSoft.vRPC;
using DanilovSoft.WebSockets;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Test
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri("ws://echo.websocket.org"), default);

            var buff2 = new byte[] { 1, 2 };
            await ws.SendAsync(buff2, System.Net.WebSockets.WebSocketMessageType.Binary, true, default);

            var buf = new byte[0];
            var res = await ws.ReceiveAsync(buf, default);

            var mem = new MemoryStream(FromHex("082e10151843"));
            var header = ProtoBuf.Serializer.Deserialize<HeaderDto>(mem);
        }

        public static byte[] FromHex(string hex)
        {
            var result = new byte[hex.Length / 2];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return result;
        }
    }
}
