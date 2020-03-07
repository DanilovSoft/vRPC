﻿using DanilovSoft.vRPC;
using DanilovSoft.WebSockets;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Test
{
    class Program
    {
        static void Main(string[] args)
        {
            TestJwt();
        }

        public static void TestJwt()
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
                    string decriptedText = rijndaelKey.Decrypt(cipherText);

                    using (var rijndaelKey2 = new RijndaelEnhanced(passPhrase, initVector, 8, 16, 256, "qwertyuiqwertyuiqwertyui", 1000))
                    {
                        string decr = rijndaelKey2.Decrypt(cipherText);
                    }
                }

                // Make sure we got decryption working correctly.
                Console.WriteLine($"\nDecrypted   : {plainText}");
            }
        }
    }
}
