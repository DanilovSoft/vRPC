using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Decorator;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp
{
    class Program
    {
        static async Task Main()
        {
            //HeaderDto h = HeaderDto.CreateRequest(int.MaxValue, int.MaxValue, "json", "Home/Test");
            //while (true)
            //{
                //var arrayWriter = new ArrayBufferWriter<byte>(1024);
            //    var sw = Stopwatch.StartNew();
            //    h.SerializeJson(arrayWriter);
            //    sw.Stop();
            //    Trace.WriteLine("System.Text Ser: " + sw.ElapsedTicks);

            //    string controlJ = Encoding.UTF8.GetString(arrayWriter.WrittenSpan);

            //    var mem2 = new MemoryStream(64);
            //    sw.Restart();
            //    ProtoBuf.Serializer.Serialize<HeaderDto>(mem2, h);
            //    sw.Stop();
            //    Trace.WriteLine("ProtoBuf Ser: " + sw.ElapsedTicks);

            //    sw.Restart();
            //    var h4 = RequestContentParser.DeserializeHeader(arrayWriter.WrittenSpan);
            //    //var h2 = System.Text.Json.JsonSerializer.Deserialize<HeaderDto>(arraywriter.WrittenSpan);
            //    sw.Stop();
            //    Trace.WriteLine("System.Text Deser: " + sw.ElapsedTicks);

            //    mem2.Position = 0;
            //    sw.Restart();
            //    var h3 = ProtoBuf.Serializer.Deserialize<HeaderDto>(mem2);
            //    sw.Stop();
            //    Trace.WriteLine("ProtoBuf Deser: " + sw.ElapsedTicks);
            //}

            var listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();
            var client = new VRpcClient("localhost", port: 1234, ssl: false, allowAutoConnect: true);
            client.Connect();
            var proxy = client.GetProxy<IBenchmark>();

            while (true)
            {
                proxy.VoidOneArg(123);
            }
        }
    }

    public interface IBenchmark
    {
        [TcpNoDelay]
        int VoidOneArg(int n);
    }
}
