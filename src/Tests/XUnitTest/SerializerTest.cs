using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace InternalXUnitTest
{
    public class StubController : ServerController
    {
        public void Subtract() { }
    }

    public class SerializerTest
    {
        [Fact]
        public void Deserialize()
        {
            var methods = new InvokeActionsDictionary(new Dictionary<string, Type> { ["Stub"] = typeof(StubController) });

            string json = @"{""jsonrpc"": ""2.0"", ""method"": ""Stub/Subtract"", ""params"": [42, 23], ""id"": 1.114}";
        }

        [Fact]
        public void Deserialize2()
        {
            var methods = new InvokeActionsDictionary(new Dictionary<string, Type> { ["Stub"] = typeof(StubController) });

            string json = @"{""jsonrpc"": ""2.0"", ""params"": [42, 23], ""method"": ""Stub/Subtract"", ""id"": 1}";
        }
    }
}
