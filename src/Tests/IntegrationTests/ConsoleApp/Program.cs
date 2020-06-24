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
            HeaderDto h = HeaderDto.CreateRequest(123, 10, "json", "Home/Test");
            while (true)
            {
                var arraywriter = new ArrayBufferWriter<byte>(256);
                var writer = new System.Text.Json.Utf8JsonWriter(arraywriter);

                var sw = Stopwatch.StartNew();
                System.Text.Json.JsonSerializer.Serialize<HeaderDto>(writer, h);
                sw.Stop();
                Trace.WriteLine("System.Text Ser: " + sw.ElapsedTicks);

                string controlJ = Encoding.UTF8.GetString(arraywriter.WrittenSpan);

                var mem2 = new MemoryStream(64);
                sw.Restart();
                ProtoBuf.Serializer.Serialize<HeaderDto>(mem2, h);
                sw.Stop();
                Trace.WriteLine("ProtoBuf Ser: " + sw.ElapsedTicks);

                sw.Restart();
                var h4 = RequestContentParser.DeserializeHeader(arraywriter.WrittenSpan);
                //var h2 = System.Text.Json.JsonSerializer.Deserialize<HeaderDto>(arraywriter.WrittenSpan);
                sw.Stop();
                Trace.WriteLine("System.Text Deser: " + sw.ElapsedTicks);

                mem2.Position = 0;
                sw.Restart();
                var h3 = ProtoBuf.Serializer.Deserialize<HeaderDto>(mem2);
                sw.Stop();
                Trace.WriteLine("ProtoBuf Deser: " + sw.ElapsedTicks);
            }

            var listener = new VRpcListener(IPAddress.Any, 1234);
            listener.Start();
            var client = new VRpcClient("localhost", port: 1234, ssl: false, allowAutoConnect: true);

            client.GetProxy<IBenchmark>().VoidOneArg(123);
        }
    }

    public interface IBenchmark
    {
        int VoidOneArg(int n);
    }
}
