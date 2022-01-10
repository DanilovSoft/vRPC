using Microsoft.Extensions.Logging;
using System;
using DanilovSoft.vRPC;

namespace Server.Controllers
{
    [AllowAnonymous]
    internal class BenchmarkController : RpcController
    {
        private readonly ILogger _logger;
        private readonly Program _program;
        private readonly IClientHomeController _proxy;

        public BenchmarkController(ILogger<BenchmarkController> logger, Program program, IProxy<IClientHomeController> proxy)
        {
            _logger = logger;
            _program = program;
            _proxy = proxy.Proxy;
        }

        [TcpNoDelay]
        public void VoidNoArgs()
        {

        }

        [TcpNoDelay]
        public void VoidOneArg(int n)
        {

        }

        public void PlainByteArray(byte[] data)
        {
            //Interlocked.Increment(ref Program.ReqCount);
        }
        
        public int Sum(int x1, int x2)
        {
            return unchecked(x1 + x2);
        }

        public void MultipartByteArray(RentedMemory data)
        {
            //Interlocked.Increment(ref Program.ReqCount);
        }

        public void MultipartOnlyInt(int n)
        {

        }

        public void JsonOnlyInt(int n)
        {

        }

        public void Test()
        {
            //var proxy = Context.GetProxy<ITest>();
            //proxy.Bla();
        }

        private async void PrivateTest()
        {
            try
            {
                var cr = await Connection.ShutdownAsync(TimeSpan.FromSeconds(100), "test");
            }
            catch (Exception ex)
            {

                throw;
            }
        }
    }

    public interface Controller
    {
        void Bla();
    }

    public interface I
    {
        void Bla();
    }

    public interface ITest
    {
        void Bla();
    }
}
