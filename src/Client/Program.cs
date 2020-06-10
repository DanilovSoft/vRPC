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
            try
            {
                controller.Test2();
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

    public interface IHome
    {
        Task<int> Test();
        Task Test2();
    }

    public interface IMultipart
    {
        void Test2();
        //int Test2();
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

    //public class Proxy : ClientInterfaceProxy<IHome>
    //{
    //    public Task<int> Test()
    //    {
    //        return Invoke(null, null);   
    //    }
    //}
}
