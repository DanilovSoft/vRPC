using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;
using DanilovSoft.vRPC.Source;

namespace DanilovSoft.vRPC
{
    //public class MultipartContent : VRpcContent, IEnumerable<VRpcContent>
    //{
    //    private readonly List<VRpcContent> _list = new List<VRpcContent>();

    //    public MultipartContent()
    //    {
            
    //    }

    //    public void Add(VRpcContent content)
    //    {
    //        _list.Add(content);
    //    }

    //    public IEnumerator<VRpcContent> GetEnumerator() 
    //        => _list.GetEnumerator();

    //    IEnumerator IEnumerable.GetEnumerator() 
    //        => GetEnumerator();

    //    protected override void Dispose(bool disposing)
    //    {
    //        if (disposing)
    //        {
    //            foreach (var list in _list)
    //            {
    //                list.Dispose();
    //            }
    //        }

    //        base.Dispose(disposing);
    //    }

    //    protected internal override bool TryComputeLength(out int length)
    //    {
    //        length = 0;
    //        foreach (var content in _list)
    //        {
    //            if (content.TryComputeLength(out int subLength))
    //            {
    //                checked { length += subLength; }
    //            }
    //            else
    //            {
    //                length = -1;
    //                return false;
    //            }
    //        }
    //        return true;
    //    }

    //    private protected override Multipart SerializeToStream(Stream stream)
    //    {
    //        foreach (var content in _list)
    //        {
    //            content.InnerSerializeToStream(stream);
    //        }
    //    }
    //}
}
