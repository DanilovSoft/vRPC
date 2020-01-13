using DanilovSoft.vRPC;
using DanilovSoft.WebSockets;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
    class Program
    {
        class FileDescription
        {
            public string FileName { get; set; }
        }


        interface ITest
        {
            //StreamCall CompressFile(FileDescription fileDescription);
        }

        static async Task Main(string[] args)
        {
            using (var client = new RpcClient(new Uri($"ws://127.0.0.1:1234")))
            {
                var test = client.GetProxy<ITest>();

                //using (var call = test.CompressFile(new FileDescription { FileName = "test.jpg" }))
                //{
                //    await call.RequestStream.WriteAsync(123);
                //    await call.RequestStream.CompleteAsync();

                //    await foreach (int resp in call.ResponseAsync)
                //    {

                //    }
                //}
            }
        }

        static void Test()
        {
            
        }
    }
}
