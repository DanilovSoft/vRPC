using Xunit;

namespace AspNetCoreXUnits
{
    public class ResponseKeyTest
    {
        private const string RequestKey = "FZW6qmoUGhS/Q+pLaES4Ig==";
        private const string ResponseKey = "Lp3lzXFRr4TGfaydcbDwmaIGGLI=";

        [Fact]
        public void CreateValidKey()
        {
            string responseKey = HandshakeHelpers.CreateResponseKey(RequestKey);
            Assert.Equal(ResponseKey, responseKey);
        }
    }
}
