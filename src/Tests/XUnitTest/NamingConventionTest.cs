using DanilovSoft.vRPC;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace XUnitTest
{
    public class NamingConventionTest
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
    }

    public interface I { }
    public interface IController { }

    [SuppressMessage("Стиль", "IDE1006:Стили именования", Justification = "Требуется для теста")]
    public interface Controller { }
    public interface IHomeController { }
}
