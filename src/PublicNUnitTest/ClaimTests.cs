using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace XUnitTest
{
    public class ClaimTests
    {
        [Test]
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

            Assert.AreEqual(claim.Type, clonedClaim.Type);
            Assert.AreEqual(claim.Value, clonedClaim.Value);
            Assert.AreEqual(claim.ValueType, clonedClaim.ValueType);
            Assert.AreEqual(claim.Issuer, clonedClaim.Issuer);
            Assert.AreEqual(claim.OriginalIssuer, clonedClaim.OriginalIssuer);
            Assert.AreEqual(claim.Properties.Count, clonedClaim.Properties.Count);
            Assert.AreEqual(claim.Properties.ElementAt(0), clonedClaim.Properties.ElementAt(0));
            Assert.AreEqual(claim.Properties.ElementAt(1), clonedClaim.Properties.ElementAt(1));
        }
    }
}
