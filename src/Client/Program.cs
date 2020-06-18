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
            using var listener = new VRpcListener(IPAddress.Any, 1002);
            listener.Start();
            using var client = new VRpcClient("127.0.0.1", 1002, false, true);
            client.Connect();

            var controller = client.GetProxy<IMyServer>();
            try
            {
                await controller.Test2();
                await controller.Test2();
                await controller.Test2();
                await controller.Test2();

                //var t1 = Task.Run(() => controller.Test2().AsTask());
                //var t3 = Task.Run(() => controller.Test2().AsTask());
                //var t4 = Task.Run(() => controller.Test2().AsTask());

                //await Task.WhenAll(t1, t2);
            }
            catch (Exception ex)
            {
                Debugger.Break();
                throw;
            }

            listener.Shutdown(TimeSpan.FromSeconds(2));
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

    public interface IHome
    {
        Task<int> Test();
        Task Test2();
    }

    public interface IMyServer
    {
        [Notification]
        ValueTask Test2();
        
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

    public readonly struct TestStruct
    {
        public int Value { get; }

        public TestStruct(int value)
        {
            Value = value;
        }
    }
}
