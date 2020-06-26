using DanilovSoft.vRPC;
using System;
using Xunit;

namespace XUnitTest
{
    public class UnitTest1
    {
        [Fact]
        public void TestInterfaceNaming()
        {
            var rpc = new VRpcClient("127.0.0.1", 1234, false, true);

            var decorator = rpc.GetProxyDecorator<IHomeController>();
            Assert.Equal("Home", decorator.ControllerName);

            try
            {
                rpc.GetProxy<I>();
                Assert.True(false);
            }
            catch (VRpcException)
            {
                // OK
            }

            try
            {
                rpc.GetProxy<IController>();
                Assert.True(false);
            }
            catch (VRpcException)
            {
                // OK
            }

            try
            {
                rpc.GetProxy<Controller>();
                Assert.True(false);
            }
            catch (VRpcException)
            {
                // OK
            }
        }

        [Fact]
        public void TestDebugValidator()
        {
            //DanilovSoft.vRPC.Decorator.DebugOnly.ValidateIsInstanceOfType(TimeSpan.Zero, typeof(TimeSpan));
        }
    }

    public interface I { }
    public interface IController { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Стиль", "IDE1006:Стили именования", Justification = "Требуется для теста")]
    public interface Controller { }
    public interface IHomeController { }
}
