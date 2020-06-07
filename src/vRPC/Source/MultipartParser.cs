using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using DanilovSoft.vRPC.DTO;
using ProtoBuf;

namespace DanilovSoft.vRPC.Source
{
    internal static class MultipartParser
    {
        public static bool TryDeserializeMultipart(ReadOnlyMemory<byte> content, InvokeActionsDictionary invokeActions,
            HeaderDto header,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(false)]
#endif
            out RequestToInvoke? result,
#if !NETSTANDARD2_0 && !NET472
            [MaybeNullWhen(true)]
#endif
            out IActionResult? error)
        {
            //using (var stream = new ReadOnlyMemoryStream(content))
            //{
            //    do
            //    {
            //        var partHeader = Serializer.DeserializeWithLengthPrefix<MultipartHeaderDto>(stream, PrefixStyle.Base128, 1);


            //        using (var argStream = new ReadOnlyMemoryStream(content.Slice((int)stream.Position, partHeader.Size)))
            //        {
            //            var arg0 = Serializer.NonGeneric.Deserialize(typeof(int), argStream);
            //        }

            //        //stream.Position += 1;
            //        stream.Position += partHeader.Size;

            //    } while (stream.Position < stream.Length);
            //}

            throw new NotImplementedException();
        }
    }
}
