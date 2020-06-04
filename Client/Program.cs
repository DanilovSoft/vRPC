using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Decorator;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    class Program
    {
        static async Task Main()
        {
            using var listener = new RpcListener(IPAddress.Any, 1234);
            listener.Start();
            using var client = new RpcClient("127.0.0.1", 1234, false, true);
            client.Connect();

            var multipart = client.GetProxy<IMultipart>();

            using (var mem = MemoryPool<byte>.Shared.Rent(-1))
            {
                new Random().NextBytes(mem.Memory.Span);
                using (var content = new ReadOnlyMemoryContent(mem.Memory))
                {
                    using var multipartContent = new MultipartContent();
                    using var connectionId = new ProtobufValueContent(1);
                    //using var data = new ReadOnlyMemoryContent(new byte[] { 1, 2, 3 });

                    multipartContent.Add(connectionId);
                    //multipartContent.Add(data);

                    try
                    {
                        multipart.TcpData(connectionId);
                    }
                    catch (Exception ex)
                    {

                    }
                }
            }
        }
    }

    public interface IAccountController
    {
        BearerToken GetToken(string name, string password);
    }

    public interface IAdmin
    {
        void TestAdmin();
    }

    public interface IMultipart
    {
        Task<int> TcpDataAsync(int connectionData);
        void TcpData(VRpcContent connectionData);
    }
}
