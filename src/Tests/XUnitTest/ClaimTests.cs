using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace XUnitTest
{
    public class ClaimTests
    {
        [Fact]
        //[ActiveIssue(22858, framework: TargetFrameworkMonikers.NetFramework)] // Roundtripping claim fails in full framework with EndOfStream exception
        public void BinaryWriteReadTest_Success()
        {
            var claim = new Claim(ClaimTypes.Actor, "value", ClaimValueTypes.String, "issuer", "originalIssuer");
            claim.Properties.Add("key1", "val1");
            claim.Properties.Add("key2", "val2");

            Claim clonedClaim = null;
            using (var memoryStream = new MemoryStream())
            {
                using (var binaryWriter = new BinaryWriter(memoryStream, Encoding.Default, true))
                {
                    claim.WriteTo(binaryWriter);
                    binaryWriter.Flush();
                }

                memoryStream.Position = 0;
                using (var binaryReader = new BinaryReader(memoryStream))
                {
                    clonedClaim = new Claim(binaryReader);
                }
            }

            Assert.Equal(claim.Type, clonedClaim.Type);
            Assert.Equal(claim.Value, clonedClaim.Value);
            Assert.Equal(claim.ValueType, clonedClaim.ValueType);
            Assert.Equal(claim.Issuer, clonedClaim.Issuer);
            Assert.Equal(claim.OriginalIssuer, clonedClaim.OriginalIssuer);
            Assert.Equal(claim.Properties.Count, clonedClaim.Properties.Count);
            Assert.Equal(claim.Properties.ElementAt(0), clonedClaim.Properties.ElementAt(0));
            Assert.Equal(claim.Properties.ElementAt(1), clonedClaim.Properties.ElementAt(1));
        }
    }
}
