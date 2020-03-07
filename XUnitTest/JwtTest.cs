using DanilovSoft.vRPC;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace XUnitTest
{
    public class JwtTest
    {
        [Fact]
        public void TestJwt()
        {
            string plainText = "Проверка!";    // original plaintext
            string passPhrase = "Pas5pr@sePas5pr@sePas5pr@sePas5pr@sePas5pr@se";        // can be any string
            string initVector = "@1B2c3D4e5F6g7H8"; // must be 16 bytes

            // Before encrypting data, we will append plain text to a random
            // salt value, which will be between 4 and 8 bytes long (implicitly
            // used defaults).
            using (var rijndaelKey = new RijndaelEnhanced(passPhrase, initVector, 8, 16, 256, "qwertyuiqwertyuiqwertyui", 1000))
            {
                // Encrypt the same plain text data 10 time (using the same key,
                // initialization vector, etc) and see the resulting cipher text;
                // encrypted values will be different.
                for (int i = 0; i < 10; i++)
                {
                    string cipherText = rijndaelKey.Encrypt(plainText);
                    string decripted = rijndaelKey.Decrypt(cipherText);
                    Assert.Equal(plainText, decripted);

                    using (var rijndaelKey2 = new RijndaelEnhanced(passPhrase, initVector, 8, 16, 256, "qwertyuiqwertyuiqwertyui", 1000))
                    {
                        string decripted2 = rijndaelKey2.Decrypt(cipherText);
                        Assert.Equal(plainText, decripted2);
                    }
                }
            }
        }
    }
}
