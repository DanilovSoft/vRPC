using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using vRPC;

namespace Client
{
    [ControllerContract("Home")]
    public interface IHomeController
    {
        Task<string> EchoAsync();
        ValueTask Test3Async();
        ValueTask<int> Test4Async();
    }
}
