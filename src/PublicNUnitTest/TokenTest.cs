using DanilovSoft.vRPC;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace XUnitTest
{
    public class TokenTest
    {
        [Test]
        public void Token()
        {
            var token = new AccessToken(new byte[] { 1, 2, 3 });
            string j = JsonSerializer.Serialize(token);
        }
    }
}
