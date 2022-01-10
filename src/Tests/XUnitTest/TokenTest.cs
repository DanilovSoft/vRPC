using DanilovSoft.vRPC;
using System.Text.Json;
using Xunit;

namespace XUnitTest
{
    public class TokenTest
    {
        [Fact]
        public void Token()
        {
            var token = new AccessToken(new byte[] { 1, 2, 3 });
            string j = JsonSerializer.Serialize(token);
        }
    }
}
