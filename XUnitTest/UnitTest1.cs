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
            var rpc = new RpcClient("127.0.0.1", 1234);

            var decorator = rpc.GetProxyDecorator<IHomeController>();
            Assert.Equal("Home", decorator.ControllerName);

            try
            {
                rpc.GetProxy<I>();
                Assert.True(false);
            }
            catch (VRpcException)
            {
                
            }

            try
            {
                rpc.GetProxy<IController>();
                Assert.True(false);
            }
            catch (VRpcException)
            {

            }

            try
            {
                rpc.GetProxy<Controller>();
                Assert.True(false);
            }
            catch (VRpcException)
            {

            }   
        }
    }

    public interface I { }
    public interface IController { }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("�����", "IDE1006:����� ����������", Justification = "��������� ��� �����")]
    public interface Controller { }
    public interface IHomeController { }
}