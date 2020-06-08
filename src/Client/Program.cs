using DanilovSoft.vRPC;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Decorator;
using Newtonsoft.Json;
using System;
using System.Buffers;
using System.Diagnostics;
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

            var controller = client.GetProxy<IMultipart>();

            var mem = MemoryPool<byte>.Shared.Rent(-1);
            //new Random().NextBytes(mem.Memory.Span);
            using var data = new PooledMemoryContent(mem, mem.Memory.Slice(0, 10));
            using var connectionId = new ProtobufValueContent(123);

            try
            {
                await controller.TcpData(connectionId, data);
            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
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
        int TcpData(int connectionData);
        Task<int> TcpDataAsync(int connectionData, byte[] data);
        Task TcpData(VRpcContent connectionId, PooledMemoryContent data);
    }

    public class PooledMemoryContent : ReadOnlyMemoryContent
    {
        private IMemoryOwner<byte> _mem;

        public PooledMemoryContent(IMemoryOwner<byte> mem, ReadOnlyMemory<byte> slice) : base (slice)
        {
            _mem = mem;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Interlocked.Exchange(ref _mem, null)?.Dispose();
            }
        }
    }
}
