using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DanilovSoft.vRPC.Content;

namespace DanilovSoft.vRPC
{
    public class MultipartContent : VRpcContent, IEnumerable<VRpcContent>
    {
        private readonly List<VRpcContent> _list = new List<VRpcContent>();

        public MultipartContent()
        {
            
        }

        public void Add(VRpcContent content)
        {
            _list.Add(content);
        }

        public IEnumerator<VRpcContent> GetEnumerator() 
            => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() 
            => GetEnumerator();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var list in _list)
                {
                    list.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        protected internal override bool TryComputeLength(out long length)
        {
            length = 0;
            foreach (var content in _list)
            {
                if (content.TryComputeLength(out long subLength))
                {
                    checked { length += subLength; }
                }
                else
                {
                    length = -1;
                    return false;
                }
            }
            return true;
        }

        protected internal override async Task SerializeToStreamAsync(Stream stream)
        {
            foreach (var content in _list)
            {
                await content.SerializeToStreamAsync(stream).ConfigureAwait(false);
            }
        }
    }
}
